/**
 * Helpers for StoredProcResultFlat messages from /hubs/sp (same "flat" format as TMS):
 *   { procedure, data: { status: 'Success', data: [{ columns, rows }] } | { status: 'Failed', message } }
 * or an array of those for batch calls.
 */
export interface FlatResult {
  procedure: string;
  data: FlatExecution | FlatExecution[];
}

interface FlatExecution {
  status: 'Success' | 'Failed';
  message?: string;
  data?: { columns: string[]; rows: unknown[][] }[];
}

export type SpTables = Record<string, Record<string, any>[]>;

/** Procedure name without the database / schema prefix the TMS screens send ('iplex_srcdb01.dbo.web_rd_x' -> 'web_rd_x'). */
export const procName = (name: string | null | undefined): string => (name ?? '').split('.').pop()!.replace(/^\[|\]$/g, '');

/** Result sets as arrays of row objects: { table0: [...], table1: [...] } (TMS mergeExecutionsToJson). */
export function spTables(res: FlatResult): SpTables {
  const merged: SpTables = {};
  const executions = Array.isArray(res.data) ? res.data : [res.data];
  for (const execution of executions) {
    if (execution?.status !== 'Success' || !Array.isArray(execution.data)) continue;
    execution.data.forEach((table, index) => {
      const rows = table.rows.map((row) => Object.fromEntries(table.columns.map((c, i) => [c, row[i]])));
      (merged[`table${index}`] ??= []).push(...rows);
    });
  }
  return merged;
}

/** The procedure's error message when the call failed (database rules come through as readable text), else null. */
export function spError(res: FlatResult): string | null {
  const executions = Array.isArray(res.data) ? res.data : [res.data];
  const failed = executions.find((e) => e?.status === 'Failed');
  return failed ? failed.message || 'The request failed.' : null;
}
