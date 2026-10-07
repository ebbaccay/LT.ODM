/**
 * Ported from TMS. LT ODM: connects to /hubs/notifications (NotificationsHub) with the LT ODM sign-in token.
 * The server takes the user group, sender and history scope from the token.
 */
import { Injectable, inject, signal, computed, effect, NgZone } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { environment } from '@env/environment';
import { UserInfoService } from './user-info.service';
import { LoggerService } from './logger.service';
import { AuthService } from '../../core/auth/auth.service';

@Injectable({ providedIn: 'root' })
export class NotificationHubService {
  private readonly _zone = inject(NgZone);
  private readonly _userInfo = inject(UserInfoService);
  private readonly _logger = inject(LoggerService);
  private readonly _auth = inject(AuthService);

  readonly hubStatus = signal<'disconnected' | 'connecting' | 'connected' | 'reconnecting'>('disconnected');
  readonly isConnected = computed(() => this.hubStatus() === 'connected');

  private hubConn?: signalR.HubConnection;
  private _initPromise: Promise<void> | null = null;
  private _retryHandle?: ReturnType<typeof setTimeout>;

  async initHub(): Promise<void> {
    if (this.hubStatus() === 'connected' || this.hubStatus() === 'connecting' || this.hubStatus() === 'reconnecting') {
      return;
    }

    if (this._initPromise) {
      await this._initPromise;
      return;
    }

    this._initPromise = this._initHubInternal();
    try {
      await this._initPromise;
    } finally {
      this._initPromise = null;
    }
  }

  private async _initHubInternal(): Promise<void> {
    const token = this._auth.getAccessToken();
    if (!token) {
      this._logger.warn('NotificationHub init skipped: missing token');
      this.hubStatus.set('disconnected');
      this._scheduleRetry();
      return;
    }

    await this.teardownHub(false);

    const options: signalR.IHttpConnectionOptions = {
      // LT ODM: current access token, refreshed first if missing.
      accessTokenFactory: async () => (this._auth.getAccessToken() ?? ((await this._auth.refresh()) ? this._auth.getAccessToken() : null)) ?? '',
      skipNegotiation: environment.skipNegotiation && environment.transport === 'WebSockets',
      transport: environment.transport === 'WebSockets' ? signalR.HttpTransportType.WebSockets : undefined
    };

    this.hubConn = new signalR.HubConnectionBuilder()
      .withUrl(environment.notificationsHubUrl, options)
      .withAutomaticReconnect([0, 2000, 5000, 10000])
      .configureLogging(environment.production ? signalR.LogLevel.Error : signalR.LogLevel.Information)
      .build();

    this.hubConn.serverTimeoutInMilliseconds = 60000;
    this.hubConn.keepAliveIntervalInMilliseconds = 15000;

    this.hubConn.onreconnecting(() => {
      this._zone.run(() => this.hubStatus.set('reconnecting'));
    });

    this.hubConn.onreconnected(async () => {
      this._zone.run(() => this.hubStatus.set('connected'));
      // Server assigns a new connection ID on every reconnect and clears all
      // group memberships — re-register so notifications keep flowing.
      await this._registerUser();
    });

    this.hubConn.onclose(() => {
      this._zone.run(() => this.hubStatus.set('disconnected'));
      this._scheduleRetry();
    });

    try {
      this.hubStatus.set('connecting');
      await this.hubConn.start();
      this.hubStatus.set('connected');
      this._clearRetry();

      this.registerListeners();

      await this._registerUser();
    } catch (err) {
      this._logger.error('NotificationHub Connection Failed', err);
      this.hubConn = undefined;
      this.hubStatus.set('disconnected');
      this._scheduleRetry();
    }
  }

  private async _registerUser(): Promise<void> {
    if (!this.hubConn) return;
    const userGroup = this._userInfo.user?.userGroup ?? 'TMS';
    const location  = this._userInfo.user?.location  ?? null;
    try {
      await this.hubConn.invoke('RegisterUser', userGroup, location ?? '');
    } catch (err) {
      this._logger.error('RegisterUser failed', err);
    }
  }

  private registerListeners() {
    if (!this.hubConn) return;

    this.hubConn.on('RegisterUserSuccess', (response: any) => {
      this._logger.info('Registered to group:', response?.groupName);
    });

    this.hubConn.on('RegisterUserError', (error: string) => {
      this._logger.warn('RegisterUser error:', error);
    });

    this.hubConn.on('NotificationSent', (response: any) => {
      this._logger.info('Notification sent:', response?.success);
    });

    this.hubConn.on('NotificationError', (error: string) => {
      this._logger.error('Notification error:', error);
    });

    this.hubConn.on('NotificationHistory', (_json: string) => {
      this._logger.info('Notification history loaded');
    });

    this.hubConn.on('NotificationHistoryError', (error: string) => {
      this._logger.error('Notification history error:', error);
    });
  }

  async teardownHub(clearRetry: boolean = true) {
    if (clearRetry) {
      this._clearRetry();
    }

    if (this.hubConn) {
      await this.hubConn.stop();
      this.hubConn = undefined;
    }
    this.hubStatus.set('disconnected');
  }

  async sendNotification(
    type: string,
    styleId: string,
    packName: string,
    factoryId: string,
    factoryName: string,
    sender: string,
    senderDisplay: string,
    recipientGroup: string,
    messageText: string
  ) {
    if (!this.hubConn || this.hubStatus() !== 'connected') {
      this._logger.warn('NotificationHub not connected');
      return;
    }

    try {
      await this.hubConn.invoke(
        'SendNotification',
        type,
        styleId,
        packName,
        factoryId,
        factoryName,
        sender,
        senderDisplay,
        recipientGroup,
        messageText
      );
    } catch (err) {
      this._logger.error('sendNotification failed', err);
    }
  }

  async loadNotificationHistory(recipientGroup: string, limitDays: number = 7) {
    if (!this.hubConn || this.hubStatus() !== 'connected') {
      this._logger.warn('NotificationHub not connected');
      return;
    }

    try {
      await this.hubConn.invoke('LoadNotificationHistory', recipientGroup, limitDays);
    } catch (err) {
      this._logger.error('loadNotificationHistory failed', err);
    }
  }

  private _scheduleRetry(): void {
    if (this._retryHandle) return;

    this._retryHandle = setTimeout(() => {
      this._retryHandle = undefined;
      void this.initHub();
    }, 5000);
  }

  private _clearRetry(): void {
    if (!this._retryHandle) return;
    clearTimeout(this._retryHandle);
    this._retryHandle = undefined;
  }
}
