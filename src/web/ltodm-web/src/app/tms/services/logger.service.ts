import { Injectable } from '@angular/core';
import { environment } from '@env/environment';

type LogLevelName = 'off' | 'error' | 'warn' | 'info' | 'debug';

const LevelOrder: Record<LogLevelName, number> = {
  off: 0,
  error: 1,
  warn: 2,
  info: 3,
  debug: 4,
};

@Injectable({ providedIn: 'root' })
export class LoggerService {
  private threshold: number;
  private static staticThreshold: number | undefined;
  private static originalConsole: Partial<Record<'debug' | 'info' | 'warn' | 'error' | 'log', any>> | undefined;

  constructor() {
    const level = (environment as any).logLevel as LogLevelName | undefined;
    this.threshold = LevelOrder[level || (environment.production ? 'warn' : 'debug')];
  }

  debug(message: string, ...args: any[]) {
    if (this.threshold < LevelOrder.debug) return;
    // eslint-disable-next-line no-console
    console.debug(this.format(message), ...this.sanitizeArgs(args));
  }

  info(message: string, ...args: any[]) {
    if (this.threshold < LevelOrder.info) return;
    // eslint-disable-next-line no-console
    console.info(this.format(message), ...this.sanitizeArgs(args));
  }

  warn(message: string, ...args: any[]) {
    if (this.threshold < LevelOrder.warn) return;
    // eslint-disable-next-line no-console
    console.warn(this.format(message), ...this.sanitizeArgs(args));
  }

  error(message: string, ...args: any[]) {
    if (this.threshold < LevelOrder.error) return;
    // eslint-disable-next-line no-console
    console.error(this.format(message), ...this.sanitizeArgs(args));
  }

  private format(message: string) {
    return message;
  }

  private sanitizeArgs(args: any[]) {

    return args;
    // remove sanitation, causes performance issues with large objects

    const MAX_STR = 256;
    const MAX_KEYS = 10;
    const preview = (v: any): any => {
      try {
        if (typeof v === 'string') return v.length > MAX_STR ? v.slice(0, MAX_STR) + '…' : v;
        if (v == null) return v;
        if (Array.isArray(v)) return v.slice(0, MAX_KEYS).map(preview);
        if (typeof v === 'object') {
          const out: any = {};
          let i = 0;
          for (const k of Object.keys(v)) {
            if (i++ >= MAX_KEYS) { out['…'] = '…'; break; }
            out[k] = preview(v[k]);
          }
          return out;
        }
        return v;
      } catch {
        return '[unserializable]';
      }
    };
    return args.map(preview);
  }

  // Static utilities for global console patching
  static setLevel(level: LogLevelName) {
    LoggerService.staticThreshold = LevelOrder[level];
  }

  private static ensureThreshold() {
    if (typeof LoggerService.staticThreshold === 'number') return;
    const level = (environment as any).logLevel as LogLevelName | undefined;
    LoggerService.staticThreshold = LevelOrder[level || (environment.production ? 'warn' : 'debug')];
  }

  private static sanitize(args: any[]) {

    return args;
    // remove sanitation, causes performance issues with large objects

    const MAX_STR = 256;
    const MAX_KEYS = 10;
    const preview = (v: any): any => {
      try {
        if (typeof v === 'string') return v.length > MAX_STR ? v.slice(0, MAX_STR) + '…' : v;
        if (v == null) return v;
        if (Array.isArray(v)) return v.slice(0, MAX_KEYS).map(preview);
        if (typeof v === 'object') {
          const out: any = {};
          let i = 0;
          for (const k of Object.keys(v)) {
            if (i++ >= MAX_KEYS) { out['…'] = '…'; break; }
            out[k] = preview(v[k]);
          }
          return out;
        }
        return v;
      } catch {
        return '[unserializable]';
      }
    };
    return args.map(preview);
  }

  static patchConsole() {
    // capture originals once
    if (!LoggerService.originalConsole) {
      LoggerService.originalConsole = {
        debug: console.debug?.bind(console),
        info: console.info?.bind(console),
        warn: console.warn?.bind(console),
        error: console.error?.bind(console),
        log: console.log?.bind(console),
      };
    }
    LoggerService.ensureThreshold();
    const threshold = LoggerService.staticThreshold as number;

    const wrap = (level: LogLevelName, fnName: keyof Console) => {
      const min = LevelOrder[level];
      const orig = (LoggerService.originalConsole as any)[fnName] as Function | undefined;
      return (...args: any[]) => {
        // Only forward if threshold allows
        if (threshold >= min && orig) {
          try {
            orig(`[${level}]`, ...LoggerService.sanitize(args));
          } catch {
            // swallow
          }
        }
      };
    };

    // Route to sanitized console with level gate
    if (this.staticThreshold !== 4) {
      (console as any).debug = wrap('debug', 'debug');
      (console as any).info = wrap('info', 'info');
      (console as any).warn = wrap('warn', 'warn');
      (console as any).error = wrap('error', 'error');
      (console as any).log = wrap('info', 'log');
    }
  }
}
