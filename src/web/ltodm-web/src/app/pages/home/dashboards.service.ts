import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '@env/environment';
import { Observable } from 'rxjs';

// Home page dashboards (/api/v1/dashboards). Which one a user opens with is set per role in Settings > Roles.

/** DashboardKeys in the API. Adding a dashboard: docs/components/dashboard.md (Adding a dashboard). */
export type DashboardKey = 'library' | 'designer';

export const DASHBOARD_KEYS: readonly DashboardKey[] = ['library', 'designer'];

/** Page heading and subtitle of each dashboard (translation keys). */
export const DASHBOARD_TEXT: Record<DashboardKey, { title: string; subtitle: string }> = {
  library: { title: 'home.title', subtitle: 'home.subtitle' },
  designer: { title: 'home.designer.title', subtitle: 'home.designer.subtitle' },
};

export function isDashboardKey(value: unknown): value is DashboardKey {
  return DASHBOARD_KEYS.includes(value as DashboardKey);
}

/** defaultKey: the dashboard the home page opens with (null = welcome only). available: only defaultKey, except for Admins (all). */
export interface DashboardChoice {
  defaultKey: DashboardKey | null;
  available: DashboardKey[];
}

/** DesignerChecks in the API, in the order a designer usually works. */
export type DesignerCheck =
  | 'picture'
  | 'productType'
  | 'gender'
  | 'colorways'
  | 'bom'
  | 'colorwaysOnBom'
  | 'bomColours'
  | 'bomDetails'
  | 'consumption'
  | 'suppliers';

/** count: colorways or BOM lines affected (null = the whole style). */
export interface DesignerGap {
  check: DesignerCheck;
  count: number | null;
}

/** score: share of the checks passed (0-100, rounded down; 100 = nothing missing). */
export interface DesignerStyle {
  styleId: number;
  styleNo: string;
  description: string | null;
  modelName: string | null;
  customerCode: string;
  seasonCode: string;
  imageUrl: string | null;
  sketchUrl: string | null;
  conceptId: number | null;
  colorways: number;
  bomLines: number;
  packages: number;
  outcome: 'Sampling' | 'Ordered' | 'Dropped' | null;
  score: number;
  missing: DesignerGap[];
  createdUtc: string;
  lastChangedUtc: string;
  businessUnitCode?: string | null;
  createdBy?: string | null;
  lastChangedBy?: string | null;
}

export interface DesignerConcept {
  conceptId: number;
  name: string;
  customerCode: string | null;
  seasonCode: string | null;
  status: 'Draft' | 'Ready' | 'Dropped';
  styles: number;
  lastChangedUtc: string;
}

export interface DesignerPitch {
  packageId: number;
  packageName: string;
  packageStatus: string;
  customerCode: string | null;
  styleId: number;
  styleNo: string;
  outcome: 'Sampling' | 'Ordered' | 'Dropped' | null;
  atUtc: string;
}

export interface DesignerDashboard {
  season: string | null;
  /** Every season of the user's active styles (newest first), whatever season is picked. */
  seasons: { seasonCode: string; styles: number }[];
  totals: {
    styles: number;
    complete: number;
    averageScore: number;
    colorways: number;
    bomLines: number;
    inPackages: number;
    sampling: number;
    ordered: number;
  };
  /** Styles missing each check (styles where a missing BOM or colorways blocks it are not counted). */
  checks: { check: DesignerCheck; styles: number }[];
  /** Latest change first. */
  styles: DesignerStyle[];
  concepts: DesignerConcept[];
  pitches: DesignerPitch[];
  /** The user's business unit team: its styles are listed with the user's own (empty = only the user's own). */
  teamBusinessUnits?: string[];
}

@Injectable({ providedIn: 'root' })
export class DashboardsService {
  private readonly http = inject(HttpClient);
  private readonly api = `${environment.apiBaseUrl}/api/v1/dashboards`;

  mine(): Observable<DashboardChoice> {
    return this.http.get<DashboardChoice>(`${this.api}/mine`);
  }

  /** The signed-in user's own active styles; season null = all seasons. */
  designer(season: string | null): Observable<DesignerDashboard> {
    const params = season ? new HttpParams().set('season', season) : undefined;
    return this.http.get<DesignerDashboard>(`${this.api}/designer`, { params });
  }
}
