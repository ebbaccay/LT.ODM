import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, of } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface HealthStatus {
  status: 'Healthy' | 'Degraded' | 'Unhealthy' | 'Unreachable';
  checks?: { name: string; status: string }[];
}

@Injectable({ providedIn: 'root' })
export class HealthService {
  private readonly http = inject(HttpClient);

  getHealth(): Observable<HealthStatus> {
    return this.http.get<HealthStatus>(`${environment.apiBaseUrl}/health`).pipe(
      catchError((err: HttpErrorResponse) =>
        // The API answers 503 with a JSON body when SQL Server is down.
        of<HealthStatus>(err.error?.status ? err.error : { status: 'Unreachable' }),
      ),
    );
  }
}
