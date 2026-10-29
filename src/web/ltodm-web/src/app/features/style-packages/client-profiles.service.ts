import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '@env/environment';
import { Observable } from 'rxjs';

// Client profiles (/api/v1/client-profiles, roadmap 5.13) and follow-up reminders (/api/v1/follow-ups, roadmap 5.24).

/** A client: a brand, or a customer without a brand (clientKey), with its main rules and next pitch date. */
export interface ClientSummary {
  clientKey: string;
  name: string;
  isBrand: boolean;
  accounts: number;
  styles: number;
  packages: number;
  hasProfile: boolean;
  priceMin: number | null;
  priceMax: number | null;
  currency: string | null;
  minRecycledPct: number | null;
  restrictedTerms: string | null;
  accountManager: string | null;
  mills: number;
  nextPitchDate: string | null;
  nextPitchSeason: string | null;
  /** The account with the most styles (pages that take a customer, e.g. What to offer). */
  mainCustomerCode: string | null;
}

export interface PitchDate {
  seasonCode: string | null;
  /** yyyy-MM-dd */
  pitchDate: string;
  note: string | null;
}

/** rowVer null = no profile yet. Mills: preferred mills (supplier code and name). */
export interface ClientProfile {
  clientKey: string;
  priceMin: number | null;
  priceMax: number | null;
  currency: string;
  minRecycledPct: number | null;
  restrictedTerms: string | null;
  accountManager: string | null;
  notes: string | null;
  updatedBy: string | null;
  updatedUtc: string | null;
  rowVer: string | null;
  mills: { code: string; name: string }[];
  pitchDates: PitchDate[];
}

export interface SaveClientProfile {
  rowVer: string | null;
  priceMin: number | null;
  priceMax: number | null;
  currency: string | null;
  minRecycledPct: number | null;
  restrictedTerms: string | null;
  accountManager: string | null;
  notes: string | null;
  mills: string[];
  pitchDates: PitchDate[];
}

/** A style against the client's rules: Recycled (share below the minimum), Restricted (a word on its BOM), Mill (not a preferred mill). */
export interface ClientIssue {
  styleId: number;
  kind: 'Recycled' | 'Restricted' | 'Mill';
  detail: string;
}

/** clientKey null = the client has no profile (nothing checked). */
export interface ClientCheck {
  clientKey: string | null;
  issues: ClientIssue[];
}

export type FollowUpKind = 'NotOpened' | 'NoAnswer' | 'Expiring' | 'Sample' | 'Pitch';

/** A follow-up for the signed-in user. days: since it started, or (Expiring, Pitch) until it is due. */
export interface FollowUp {
  kind: FollowUpKind;
  refKey: string;
  packageId: number | null;
  packageName: string | null;
  shareId: number | null;
  shareLabel: string | null;
  packageItemId: number | null;
  styleNo: string | null;
  clientKey: string | null;
  seasonCode: string | null;
  days: number;
  dueDate: string | null;
  note: string | null;
}

@Injectable({ providedIn: 'root' })
export class ClientProfilesService {
  private readonly http = inject(HttpClient);
  private readonly api = `${environment.apiBaseUrl}/api/v1`;

  list(): Observable<ClientSummary[]> {
    return this.http.get<ClientSummary[]>(`${this.api}/client-profiles`);
  }

  get(clientKey: string): Observable<ClientProfile> {
    return this.http.get<ClientProfile>(`${this.api}/client-profiles/${encodeURIComponent(clientKey)}`);
  }

  save(clientKey: string, p: SaveClientProfile): Observable<void> {
    return this.http.put<void>(`${this.api}/client-profiles/${encodeURIComponent(clientKey)}`, p);
  }

  /** The package's styles against its client's profile. */
  checkPackage(packageId: number): Observable<ClientCheck> {
    return this.http.get<ClientCheck>(`${this.api}/style-packages/${packageId}/client-check`);
  }

  followUps(): Observable<FollowUp[]> {
    return this.http.get<FollowUp[]>(`${this.api}/follow-ups`);
  }

  /** days null = done for good; else put off for that many days. */
  dismiss(f: FollowUp, days: number | null): Observable<void> {
    return this.http.post<void>(`${this.api}/follow-ups/dismiss`, { kind: f.kind, refKey: f.refKey, days });
  }
}
