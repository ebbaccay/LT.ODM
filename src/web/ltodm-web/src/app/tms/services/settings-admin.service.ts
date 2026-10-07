import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '@env/environment';
import { Observable } from 'rxjs';

// Settings: roles, user access, the sidebar menu and reference lists (/api/v1/admin, Admin role only).
// Replaces the TMS SettingsAdminService (sp hub calls to tms_nav_* / tms_roles / tms_user_roles).

export interface RoleDto {
  roleId: number;
  /** Role name in the sign-in token, e.g. Merchandiser. Fixed once created. */
  name: string;
  displayName: string | null;
  description: string | null;
  userCount: number;
}

export interface SaveRoleRequest {
  roleId: number;
  name: string;
  displayName: string | null;
  description: string | null;
}

export interface UserAccessDto {
  userId: number;
  userName: string;
  displayName: string;
  email: string;
  isActive: boolean;
  userGroup: string | null;
  location: string | null;
  lastLoginUtc: string | null;
  lockoutEndUtc: string | null;
  roles: string[];
}

export interface SetUserAccessRequest {
  roles: string[];
  userGroup: string | null;
  location: string | null;
}

/** New user: no password, they set it from the emailed link. */
export interface CreateUserRequest {
  userName: string;
  email: string;
  displayName: string;
  roles: string[];
  userGroup: string | null;
  location: string | null;
}

export interface MenuConfigItemDto {
  itemId: number;
  text: string;
  route: string;
  icon: string;
  sortOrder: number;
  isVisible: boolean;
  /** Empty = every signed-in user. */
  allowedRoles: string[];
}

export interface MenuConfigGroupDto {
  groupId: number;
  text: string;
  icon: string;
  slot: 'main' | 'bottom';
  sortOrder: number;
  isVisible: boolean;
  items: MenuConfigItemDto[];
}

export interface SaveMenuGroupRequest {
  groupId: number;
  text: string;
  icon: string;
  slot: 'main' | 'bottom';
  sortOrder: number;
  isVisible: boolean;
}

export interface SaveMenuItemRequest {
  itemId: number;
  groupId: number;
  text: string;
  route: string;
  icon: string;
  sortOrder: number;
  isVisible: boolean;
  roles: string[];
}

/** A pick list the Reference lists page can maintain, with the API's rules for its form. */
export interface RefListInfo {
  /** customers, seasons, seasonTerms, businessUnits, productTypes, weaveTypes, contentClasses, materialTypes, uoms, suppliers */
  key: string;
  codeMaxLength: number;
  /** 'upper' / 'lower': the API stores codes in this case; '' = as typed. */
  codeCase: '' | 'upper' | 'lower';
  /** Extra code rule (JS-compatible regex), checked after the case is applied. */
  codePattern: string | null;
  nameMaxLength: number;
  nameRequired: boolean;
  hasSortOrder: boolean;
  hasIsActive: boolean;
}

/** One row of a list. In use (usageCount > 0) = cannot be deleted; make it inactive instead where the list allows. */
export interface RefListItem {
  /** Key used by styles, BOM lines and the workbook import. Fixed once created. */
  code: string;
  name: string | null;
  sortOrder: number | null;
  isActive: boolean | null;
  usageCount: number;
}

export interface SaveRefListItemRequest {
  isNew: boolean;
  code: string;
  name: string;
  sortOrder: number | null;
  isActive: boolean | null;
}

@Injectable({ providedIn: 'root' })
export class SettingsAdminService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/v1/admin`;

  // ----- Roles -----
  getRoles(): Observable<RoleDto[]> {
    return this.http.get<RoleDto[]>(`${this.base}/roles`);
  }
  saveRole(r: SaveRoleRequest): Observable<{ roleId: number }> {
    return this.http.post<{ roleId: number }>(`${this.base}/roles`, r);
  }
  deleteRole(name: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/roles/${encodeURIComponent(name)}`);
  }

  // ----- Users -----
  getUsers(): Observable<UserAccessDto[]> {
    return this.http.get<UserAccessDto[]>(`${this.base}/users`);
  }
  setUserAccess(userId: number, r: SetUserAccessRequest): Observable<void> {
    return this.http.put<void>(`${this.base}/users/${userId}/access`, r);
  }
  /** Creates the user and emails them a one-time link to set their password. */
  createUser(r: CreateUserRequest): Observable<{ userId: number }> {
    return this.http.post<{ userId: number }>(`${this.base}/users`, r);
  }
  /** Emails a new one-time "set your password" link; older links stop working. */
  sendPasswordLink(userId: number): Observable<void> {
    return this.http.post<void>(`${this.base}/users/${userId}/password-link`, null);
  }

  // ----- Menu -----
  getMenu(): Observable<MenuConfigGroupDto[]> {
    return this.http.get<MenuConfigGroupDto[]>(`${this.base}/menu`);
  }
  saveGroup(r: SaveMenuGroupRequest): Observable<{ groupId: number }> {
    return this.http.post<{ groupId: number }>(`${this.base}/menu/groups`, r);
  }
  deleteGroup(groupId: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/menu/groups/${groupId}`);
  }
  saveItem(r: SaveMenuItemRequest): Observable<{ itemId: number }> {
    return this.http.post<{ itemId: number }>(`${this.base}/menu/items`, r);
  }
  deleteItem(itemId: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/menu/items/${itemId}`);
  }

  // ----- Reference lists -----
  getRefLists(): Observable<RefListInfo[]> {
    return this.http.get<RefListInfo[]>(`${this.base}/ref-lists`);
  }
  getRefList(list: string): Observable<RefListItem[]> {
    return this.http.get<RefListItem[]>(`${this.base}/ref-lists/${list}`);
  }
  saveRefListItem(list: string, r: SaveRefListItemRequest): Observable<{ code: string }> {
    return this.http.post<{ code: string }>(`${this.base}/ref-lists/${list}`, r);
  }
  /** The code goes in the query string: codes typed in the Styles forms may contain '/'. */
  deleteRefListItem(list: string, code: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/ref-lists/${list}`, { params: { code } });
  }
}

/** Readable message from an /api/v1/admin error (ProblemDetails title or the first validation error). */
export function adminErrorMessage(err: unknown): string {
  if (err instanceof HttpErrorResponse) {
    const body = err.error as { title?: string; errors?: Record<string, string[]> } | null;
    const firstError = body?.errors ? Object.values(body.errors).flat()[0] : undefined;
    if (firstError) return firstError;
    if (err.status === 400 && body?.title) return body.title;
    if (err.status === 403) return 'You need the Admin role to change settings.';
    if (err.status === 0) return 'The server could not be reached. Check your connection and try again.';
  }
  return 'The change could not be saved. Please try again.';
}
