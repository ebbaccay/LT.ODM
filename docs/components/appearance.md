# Appearance (theme panel)

**Where:** the palette icon in the top bar. **Who:** every signed-in user. Choices are saved **in the browser** (local storage `ltodm.layout`): they follow the user on that browser only, need no server setting and are not shared with other users.

| Choice | Options | Default |
|---|---|---|
| Mode | Light, Dark, System (follows the operating system) | System |
| Primary colour | LT Blue, Noir (follows the surface), 17 preset colours, or any custom colour | LT Blue |
| Surface | Grey family for backgrounds, cards and borders: Neutral, Stone, Zinc, Slate, Gray | Zinc |
| Glass effect | On, Off | On |
| Radius | Corner rounding: 0, 0.3, 0.5, 0.625, 1 | 0.625 |
| Menu | Static (sidebar stays open on desktop), Overlay (slides over the page) | Static |

## Companion colours

Charts and highlights (for example on the [Dashboard](dashboard.md)) use four colours worked out from the primary colour, so pages are not one colour: the primary itself, the colours 60° either side of it on the colour wheel, and its complement (opposite). With LT Blue these are blue, teal, magenta and amber. They change with the primary colour, custom colours included. Text always uses the normal text colours. Browsers too old for this get fixed teal, amber and magenta.

## Glass effect

When on, cards, the sidebar, the top bar, menus and popovers are translucent and blurred over a soft, still glow of the companion colours behind the page.

- **Dialogs stay almost solid** so their contents are always readable.
- **Data grids stay solid** so figures stay easy to read.
- Panels are solid and the glow is hidden when the browser cannot blur, when the user has turned transparency effects off in the operating system, in high-contrast mode, and when printing.
- On slow machines, switching it off makes scrolling lighter.
