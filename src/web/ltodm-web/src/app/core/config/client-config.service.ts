import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface ClientConfig {
  agGridLicenseKey: string;
}

/**
 * Runtime settings served by the API (GET /api/v1/client-config), so keys such as the
 * AG Grid licence stay in user secrets / environment variables instead of source control.
 */
@Injectable({ providedIn: 'root' })
export class ClientConfigService {
  private readonly http = inject(HttpClient);

  load(): Promise<ClientConfig | null> {
    return firstValueFrom(this.http.get<ClientConfig>(`${environment.apiBaseUrl}/api/v1/client-config`)).catch(() => null);
  }
}
