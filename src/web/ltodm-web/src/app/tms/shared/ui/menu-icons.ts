import {
  lucideBadgeCheck,
  lucideBell,
  lucideBookOpen,
  lucideBox,
  lucideBriefcase,
  lucideBuilding2,
  lucideCalculator,
  lucideCalendar,
  lucideChartColumn,
  lucideChartPie,
  lucideCircleDot,
  lucideClipboardList,
  lucideComponent,
  lucideDatabase,
  lucideFactory,
  lucideFileSpreadsheet,
  lucideFileText,
  lucideFlaskConical,
  lucideFolder,
  lucideGitCompareArrows,
  lucideGlobe,
  lucideImages,
  lucideLayers,
  lucideLayoutDashboard,
  lucideLightbulb,
  lucideListChecks,
  lucideListTree,
  lucidePackage,
  lucidePalette,
  lucidePanelsTopLeft,
  lucidePlug,
  lucideReceipt,
  lucideRuler,
  lucideScanSearch,
  lucideScissors,
  lucideSend,
  lucideSettings,
  lucideShapes,
  lucideShieldAlert,
  lucideShieldCheck,
  lucideShirt,
  lucideShoppingBag,
  lucideSparkles,
  lucideStore,
  lucideTable,
  lucideTag,
  lucideTimer,
  lucideTrendingDown,
  lucideTrendingUp,
  lucideTruck,
  lucideUsers,
  lucideWandSparkles,
  lucideWrench,
} from '@ng-icons/lucide';

/**
 * Icons that menu groups and items can use (nav.Groups.Icon / nav.Items.Icon).
 * Register with provideIcons(MENU_ICONS); the Settings > Menu icon picker lists MENU_ICON_NAMES.
 * To offer another icon, import it from @ng-icons/lucide and add it here.
 */
export const MENU_ICONS = {
  lucideLayoutDashboard, lucideImages, lucideListTree, lucideTimer, lucideCalculator, lucideComponent,
  lucideStore, lucideFactory, lucideSettings, lucideTable, lucideFolder, lucideCircleDot,
  lucideReceipt, lucideLightbulb, lucideSend, lucideLayers, lucideShapes, lucideTrendingDown, lucideFileText, lucideFileSpreadsheet,
  lucideTrendingUp, lucideBuilding2, lucideChartColumn, lucidePanelsTopLeft, lucideShieldCheck, lucideUsers, lucideListChecks,
  lucideShirt, lucideScissors, lucideRuler, lucidePalette, lucideTag, lucidePackage, lucideBox, lucideTruck,
  lucideShoppingBag, lucideClipboardList, lucideCalendar, lucideChartPie, lucideDatabase, lucideBadgeCheck,
  lucideBell, lucideBookOpen, lucideBriefcase, lucideGlobe, lucideWrench,
  lucideSparkles, lucideScanSearch, lucideGitCompareArrows, lucideShieldAlert, lucideWandSparkles,
  lucideFlaskConical, lucidePlug,
};

export const MENU_ICON_NAMES = Object.keys(MENU_ICONS);

/** Icon to show when the stored name is not registered (e.g. added in SQL by hand). */
export const FALLBACK_MENU_ICON = 'lucideCircleDot';

export const menuIcon = (name: string | null | undefined): string =>
  name && name in MENU_ICONS ? name : FALLBACK_MENU_ICON;
