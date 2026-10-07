import { Injectable, inject, signal } from '@angular/core';
import { SwUpdate } from '@angular/service-worker';
import { filter } from 'rxjs';

interface BeforeInstallPromptEvent extends Event {
  prompt(): Promise<void>;
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>;
}

@Injectable({ providedIn: 'root' })
export class PwaService {
  private readonly swUpdate = inject(SwUpdate);
  private installEvent: BeforeInstallPromptEvent | null = null;

  readonly canInstall = signal(false);
  readonly updateAvailable = signal(false);
  readonly online = signal(navigator.onLine);

  /** iPhone/iPad Safari never fires beforeinstallprompt; show the manual hint instead. */
  readonly showIosHint = signal(this.isIos() && !this.isStandalone());

  constructor() {
    window.addEventListener('beforeinstallprompt', (event) => {
      event.preventDefault();
      this.installEvent = event as BeforeInstallPromptEvent;
      this.canInstall.set(true);
    });
    window.addEventListener('appinstalled', () => this.canInstall.set(false));
    window.addEventListener('online', () => this.online.set(true));
    window.addEventListener('offline', () => this.online.set(false));

    if (this.swUpdate.isEnabled) {
      this.swUpdate.versionUpdates
        .pipe(filter((e) => e.type === 'VERSION_READY'))
        .subscribe(() => this.updateAvailable.set(true));
    }
  }

  async install(): Promise<void> {
    if (!this.installEvent) {
      return;
    }
    await this.installEvent.prompt();
    await this.installEvent.userChoice;
    this.installEvent = null;
    this.canInstall.set(false);
  }

  reload(): void {
    document.location.reload();
  }

  dismissIosHint(): void {
    this.showIosHint.set(false);
  }

  private isIos(): boolean {
    return /iphone|ipad|ipod/i.test(navigator.userAgent);
  }

  private isStandalone(): boolean {
    return matchMedia('(display-mode: standalone)').matches || (navigator as { standalone?: boolean }).standalone === true;
  }
}
