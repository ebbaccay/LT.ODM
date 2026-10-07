/**
 * Ported from TMS (main/services/spHub.service.ts). Changes for LT ODM:
 * - connects to the secured compatibility hub (environment.spHubUrl = /hubs/sp);
 * - uses the LT ODM sign-in token and session refresh; no MessagePack.
 */
import { Injectable, NgZone, signal, computed, inject, effect, WritableSignal } from '@angular/core';
import { Router } from '@angular/router';
import * as signalR from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { debounceTime, distinctUntilChanged, filter, takeUntil } from 'rxjs/operators';
import { environment } from '@env/environment';
import { LoggerService } from './logger.service';
import { UserInfoService } from './user-info.service';
import { AuthService } from '../../core/auth/auth.service';

type HubState = 'disconnected' | 'unauthorized' | 'connected' | 'reconnecting' | 'waiting' | 'refreshing';

@Injectable({ providedIn: 'root' })
export class StoredProcSignalRService {
  // 1. Dependency Injection via inject()
  private readonly _router = inject(Router);
  private readonly _zone = inject(NgZone);
  private readonly _userInfo = inject(UserInfoService);
  private readonly _auth = inject(AuthService);
  private readonly _logger = inject(LoggerService);
  private readonly _spDebugEnabled = !!(environment as Record<string, unknown>)['spDebug'];

  // 2. State management via Signals
  readonly hubStatus = signal<HubState>('disconnected');
  readonly errorMessage = signal<{ isError: boolean; message: string }>({ isError: false, message: '' });
  
  // Computed Signal for UI state
  readonly isConnected = computed(() => this.hubStatus() === 'connected');

  public hubConn?: signalR.HubConnection;
  private readonly _destroy$ = new Subject<void>();
  private readonly _listenLog = new Subject<any>();
  private pendingInvocations: Array<{ invoke: string; parm: any[] }> = [];
  private _initPromise: Promise<void> | null = null;
  private _spDebugSeq = 0;

  constructor() {
    // 3. Effect to handle side effects of state changes
    effect(() => {
      if (this.isConnected()) {
        this.flushPendingInvocations();
      }
    });
  }

  /**
   * Initializes the Hub with modern async/await patterns
   */
  async initHub(token?: string) {
    this._spTrace('initHub:enter', { hasToken: !!token });
    if (this.hubStatus() === 'connected' || this.hubStatus() === 'waiting' || this.hubStatus() === 'reconnecting') {
      this._spTrace('initHub:skip', { reason: this.hubStatus() });
      return;
    }

    if (this._initPromise) {
      await this._initPromise;
      return;
    }

    this._initPromise = this._initHubInternal(token);
    try {
      await this._initPromise;
      this._spTrace('initHub:ok');
    } finally {
      this._initPromise = null;
    }
  }

  async ensureConnected(maxWaitMs = 12000): Promise<boolean> {
    this._spTrace('ensureConnected:enter', { maxWaitMs, status: this.hubStatus() });
    if (!this.hubConn || this.hubStatus() === 'disconnected') {
      await this.initHub();
    }

    let waitedMs = 0;
    while (this.hubStatus() !== 'connected' && waitedMs < maxWaitMs) {
      await new Promise(resolve => setTimeout(resolve, 100));
      waitedMs += 100;
    }

    const ok = this.hubStatus() === 'connected';
    this._spTrace('ensureConnected:exit', { ok, waitedMs, status: this.hubStatus() });
    return ok;
  }

  private async _initHubInternal(token?: string) {
    const { spHubUrl, skipNegotiation } = environment;
    
    // Clean up existing connections
    await this.teardownHub();
    this._spTrace('hub:build', {
      url: spHubUrl,
      skipNegotiation: skipNegotiation && environment.transport === 'WebSockets',
      transport: environment.transport,
    });

    const options: signalR.IHttpConnectionOptions = {
      // LT ODM: current access token; refreshed first if it is missing (e.g. after a long idle).
      accessTokenFactory: async () => (this._auth.getAccessToken() ?? ((await this._auth.refresh()) ? this._auth.getAccessToken() : null)) ?? '',
      skipNegotiation: skipNegotiation && environment.transport === 'WebSockets',
      transport: environment.transport === 'WebSockets' ? signalR.HttpTransportType.WebSockets : undefined
    };

    this.hubConn = new signalR.HubConnectionBuilder()
      .withUrl(spHubUrl, options)
      //.withHubProtocol(new MessagePackHubProtocol()) // Use binary protocol
      .withAutomaticReconnect([0, 2000, 5000, 10000])
      .configureLogging(environment.production ? signalR.LogLevel.Error : signalR.LogLevel.Information)
      .build();

    // Standard Timeouts for SQL Heavy Workloads
    this.hubConn.serverTimeoutInMilliseconds = 180000; 
    this.hubConn.keepAliveIntervalInMilliseconds = 15000;

    this.setupListeners();
    await this.startConnection();
  }

  private async startConnection() {
    try {
      this.hubStatus.set('waiting');
      this._spTrace('hub:start');
      await this.hubConn?.start();
      this.hubStatus.set('connected');
      this._spTrace('hub:connected', { connectionId: this.hubConn?.connectionId ?? null });
    } catch (err) {
      this._logger.error('SignalR Start Failed', err);
      this._spTrace('hub:startFailed', { error: this._toDebugError(err) });
      this.hubStatus.set('disconnected');
      // Automatic retry logic
      setTimeout(() => this.startConnection(), 5000);
    }
  }

  /**
   * Efficient Invoke Gating
   */
  async invoke(method: string, ...args: any[]) {
    // Single-flight token check
    /*if (this._jwt.isTokenExpired(this._userInfo.user?.token ?? '')) {
      const refreshed = await this.tryRefreshToken();
      if (!refreshed) return;
    }*/

    if (this.hubStatus() !== 'connected') {
      this.pendingInvocations.push({ invoke: method, parm: args });
      this._spTrace('invoke:queued', {
        method,
        status: this.hubStatus(),
        pending: this.pendingInvocations.length,
        args: this._debugShape(args),
      });
      return;
    }

    this._spTrace('invoke:send', {
      method,
      args: this._debugShape(args),
      connectionId: this.hubConn?.connectionId ?? null,
    });

    // Run outside Angular for performance, re-enter zone for UI updates if needed
    this._zone.runOutsideAngular(() => {
      this.hubConn?.invoke(method, ...this.ensureUserContext(args)).catch(err => {
        this._zone.run(() => {
          this.errorMessage.set({ isError: true, message: `Invoke error: ${method}` });
          this._logger.error(`Invoke error: ${method}`, err);
          this._spTrace('invoke:failed', { method, error: this._toDebugError(err) });
        });
      });
    });
  }

  updateSignalSafe<T>(sig: WritableSignal<T>, value: T) {
  this._zone.run(() => sig.set(value));
}

  private async tryRefreshToken(): Promise<boolean> {
    this.hubStatus.set('refreshing');
    try {
      return await this._auth.refresh();
    } catch {
      this.signedOut();
      return false;
    }
  }

  private setupListeners() {
    if (!this.hubConn) return;

    this.hubConn.onreconnecting((err) => {
      this.hubStatus.set('reconnecting');
      this._spTrace('hub:reconnecting', { error: this._toDebugError(err) });
    });
    this.hubConn.onreconnected((connectionId) => {
      this.hubStatus.set('connected');
      this._spTrace('hub:reconnected', { connectionId: connectionId ?? null });
    });
    this.hubConn.onclose((err) => {
      this.hubStatus.set('disconnected');
      this._spTrace('hub:closed', { error: this._toDebugError(err) });
    });

    // Dynamic Log Listener using modern zone handling
    this.hubConn.on('WebUIErrorLog', (data) => {
      this._zone.run(() => this._listenLog.next(data));
    });
  }

  private flushPendingInvocations() {
    if (this.pendingInvocations.length === 0) return;
    
    const batch = [...this.pendingInvocations];
    this.pendingInvocations = [];
    this._spTrace('invoke:flushPending', { count: batch.length });
    
    batch.forEach(item => this.invoke(item.invoke, ...item.parm));
  }

  private ensureUserContext(parm: any[]): any[] {
    const username = this._userInfo.user?.username;
    if (username && (!parm[0] || parm[0] === '')) {
      parm[0] = username;
    }
    return parm;
  }

  private async teardownHub() {
    if (this.hubConn) {
      this._spTrace('hub:teardown', { connectionId: this.hubConn.connectionId ?? null });
      await this.hubConn.stop();
      this.hubConn = undefined;
    }
    this.hubStatus.set('disconnected');
  }

  private signedOut() {
    this.teardownHub();
    this.hubStatus.set('unauthorized');
    void this._auth.sessionExpired();
  }

  private _spTrace(event: string, payload: Record<string, unknown> = {}): void {
    if (!this._spDebugEnabled) return;
    const seq = ++this._spDebugSeq;
    const trace = {
      tag: 'SPDBG',
      seq,
      event,
      at: new Date().toISOString(),
      status: this.hubStatus(),
      ...payload,
    };
    this._logger.info('SP_TRACE', trace);
  }

  private _toDebugError(err: unknown): Record<string, unknown> {
    if (err instanceof Error) {
      return { name: err.name, message: err.message };
    }
    return { raw: String(err ?? '') };
  }

  private _debugShape(value: unknown): unknown {
    if (value === null || value === undefined) return value;
    if (Array.isArray(value)) {
      return value.slice(0, 4).map(v => this._debugShape(v));
    }
    if (typeof value === 'object') {
      const src = value as Record<string, unknown>;
      const out: Record<string, unknown> = {};
      const keys = Object.keys(src).slice(0, 10);
      for (const key of keys) {
        const lower = key.toLowerCase();
        if (lower.includes('token') || lower.includes('password')) {
          out[key] = '[redacted]';
        } else if (key === 'Parameters' && typeof src[key] === 'object' && src[key] !== null) {
          out[key] = Object.keys(src[key] as Record<string, unknown>);
        } else {
          out[key] = this._debugShape(src[key]);
        }
      }
      return out;
    }
    const str = String(value);
    return str.length > 180 ? str.slice(0, 180) + '...' : value;
  }
}