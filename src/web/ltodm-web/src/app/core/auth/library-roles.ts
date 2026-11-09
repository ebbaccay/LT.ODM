/**
 * Who may use the Style Library and the features built on it (packages, concepts, AI Studio, materials).
 * Same lists as StyleRoles in the API, which enforces them; designers create styles, so they edit like merchandisers.
 */
export const LIBRARY_READERS = ['Admin', 'Merchandiser', 'Designer', 'Costing', 'Viewer'] as const;
export const LIBRARY_EDITORS = ['Admin', 'Merchandiser', 'Designer'] as const;

/** True when one of the user's roles is in the list (role names are case-insensitive). */
export function hasAnyRole(userRoles: readonly string[] | undefined, allowed: readonly string[]): boolean {
  const wanted = allowed.map((r) => r.toLowerCase());
  return (userRoles ?? []).some((r) => wanted.includes(r.toLowerCase()));
}
