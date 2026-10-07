import { Injectable, signal } from '@angular/core';

export interface ConfirmDialogOptions {
  title:   string;
  message: string;
  confirmLabel?: string;
  cancelLabel?:  string;
  variant?: 'danger' | 'warning' | 'info';
}

interface DialogState extends ConfirmDialogOptions {
  resolve: (result: boolean) => void;
}

@Injectable({ providedIn: 'root' })
export class ConfirmDialogService {
  readonly state = signal<DialogState | null>(null);

  confirm(options: ConfirmDialogOptions): Promise<boolean> {
    return new Promise<boolean>(resolve => {
      this.state.set({ ...options, resolve });
    });
  }

  respond(result: boolean): void {
    this.state()?.resolve(result);
    this.state.set(null);
  }
}
