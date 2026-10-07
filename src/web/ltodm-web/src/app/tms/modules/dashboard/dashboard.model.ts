import { QuotationStatus, UserRole } from '../marketing/garment-quotation/garment-quotation.model';

export interface DashStatusCount {
  status: QuotationStatus;
  cnt:    number;
}

export type ActionKind = 'rfq_new' | 'rfq_revise' | 'fty_submitted' | 'fty_approved' | 'for_quotation' | 'review' | 'stale' | 'tms_sent' | 'tms_approved' | 'other';

export interface DashActionItem {
  qid:          string;
  pack_name:    string;
  style_id:     string;
  customer_id:  string;
  season_id:    string;
  factory_id:   string;
  factory_name: string;
  status:       QuotationStatus;
  fob_price:    string | null;
  price_type:   string | null;
  updated_at:   string;
  days_old:     number;
  action_kind:  ActionKind;
}

export interface DashKpis {
  total_active:       number;
  approved_count:     number;
  rejected_count:     number;
  win_rate_pct:       number;
  avg_submit_days:    number | null;
  approved_value_mtd: number;
}

export interface DashActivity {
  history_id:   number;
  qid:          string;
  pack_name:    string | null;
  style_id:     string;
  factory_id:   string;
  factory_name: string;
  status:       QuotationStatus;
  changed_by:   string;
  changed_at:   string;
}

export interface ActionGroup {
  key:    ActionKind;
  label:  string;
  hint:   string;
  tone:   'warn' | 'info' | 'success' | 'danger';
  items:  DashActionItem[];
}

export const ACTION_GROUPS_TMS: { key: ActionKind; label: string; hint: string; tone: ActionGroup['tone'] }[] = [
  { key: 'for_quotation', label: 'For Quotation',      hint: 'New styles not yet sent to any factory',              tone: 'info'    },
  { key: 'review',        label: 'Awaiting my review', hint: 'Factory submissions ready for approval or revision',  tone: 'warn'    },
  { key: 'stale',         label: 'Stale at factory',   hint: 'Sent quotations with no response — chase factory',    tone: 'danger'  },
  { key: 'tms_sent',      label: 'Sent to Factory',    hint: 'Awaiting factory costing submission',                 tone: 'info'    },
  { key: 'tms_approved',  label: 'Approved',           hint: 'Quotations approved this period',                     tone: 'success' },
];

export const ACTION_GROUPS_FTY: { key: ActionKind; label: string; hint: string; tone: ActionGroup['tone'] }[] = [
  { key: 'rfq_new',       label: 'For Quotation',      hint: 'Quotation requests waiting for your submission',  tone: 'info'    },
  { key: 'rfq_revise',    label: 'Revision requested',  hint: 'TMS asked for changes — resubmit when ready',    tone: 'warn'    },
  { key: 'fty_submitted', label: 'Submitted',           hint: 'Costing submitted — awaiting TMS review',        tone: 'warn'    },
  { key: 'fty_approved',  label: 'Approved',            hint: 'Costing approved by TMS this period',            tone: 'success' },
];

export function actionGroupsFor(role: UserRole): { key: ActionKind; label: string; hint: string; tone: ActionGroup['tone'] }[] {
  return role === 'factory' ? ACTION_GROUPS_FTY : ACTION_GROUPS_TMS;
}
