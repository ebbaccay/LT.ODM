/*
    Library assistant: seeded how-to guides (ai.Guide, ChangedHow 'Seed'). A guide is a fixed process in LT ODM as
    numbered steps, each with a link to its screen; {customer} and {season} in a link are filled from the question
    ("how do I create a package for ADI SS27?" opens New package with ADI and 2027-SS) and left out when it names none.
    The questions that open a guide at once are in seed\ai.saved-questions.sql (tool how_to, {"guide":"<code>"}).
    Steps follow docs/components (style-packages, concepts, styles, settings, ai-studio): change them together.

    Keep this file plain ASCII: sqlcmd reads it in the ANSI code page, so write curly quotes as JSON escapes
    (backslash u201c / backslash u201d) inside the steps' JSON (HowToGuideTests checks it).

    Idempotent: adds only guides that are missing, so a guide an Admin changed stays as it is.
    Run after tables\ai.tables.sql.

      sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i seed\ai.guides.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

INSERT ai.Guide (Code, Title, Who, Intro, Steps, ChangedBy, ChangedHow)
SELECT v.Code, v.Title, v.Who, v.Intro, v.Steps, N'seed', 'Seed'
FROM (VALUES
    ('create_package', N'Create a style package', N'Admin, Merchandiser, Designer',
     N'A package is the set of library styles you pitch to one client for a season.',
     N'[
        {"text":"Open Style Packages > Packages and click New package: give it a name, the customer and (optional) the season.","url":"/packages?new=1&customer={customer}&season={season}"},
        {"text":"Add styles: on the Styles list tick the styles and click Add to package. You can also add from a style''s page, its Similar tab, Image search, What to offer, a concept, or an answer of mine that lists styles.","url":"/styles?pick=1&customer={customer}"},
        {"text":"On the package page, put the styles in pitch order (Move up / Move down) and give each a note for the client, an internal note and the colourways offered.","url":"/packages?customer={customer}"},
        {"text":"Check the amber line (styles that break the client profile: recycled share, restricted materials, preferred mills) and the red banner (copies that look too close to another brand''s design).","url":null},
        {"text":"Download the Line sheet (PDF) or Excel, or share it with the client: ask me \u201chow do I share a package with a client?\u201d","url":null}
     ]'),
    ('add_styles_to_package', N'Add styles to a package', N'Admin, Merchandiser, Designer',
     N'Styles can go into a package from several places; another brand''s style goes in as a copy with its own style number.',
     N'[
        {"text":"Styles list: tick the styles (picks are kept while you search, filter and page) and click Add to package in the bar at the bottom.","url":"/styles?pick=1&customer={customer}"},
        {"text":"One style: Add to package next to Edit on the style page, or on any style of its Similar tab.","url":null},
        {"text":"Image search: Add to package on a style found, or Add N styles to a package for all of them.","url":"/ai/image-search"},
        {"text":"What to offer: tick suggestions for the client and click Add to package.","url":"/packages/offer?customer={customer}"},
        {"text":"Pick an open package (the only one for the styles'' customer is chosen for you) or start a new one. The dialog then says what happened to each style.","url":null}
     ]'),
    ('share_package', N'Share a package with a client', N'Admin, Merchandiser, Designer',
     N'The client opens a link without signing in, sees the styles as on the line sheet, and answers per style.',
     N'[
        {"text":"Open the package.","url":"/packages?customer={customer}"},
        {"text":"Under Share with client click Make link: who it is for (e.g. the men''s buying team) and how many days it works (1-90, default 30). Make one link per recipient, so the answers say who gave them.","url":null},
        {"text":"Copy the link and send it. Several people can use one link: each types a name, then Like / Pass, comments and Request sample per style.","url":null},
        {"text":"Answers show on the package page and in the bell. The first visit turns the package to Viewed, a sample request to Sampling; set Ordered or Closed yourself with Edit.","url":null},
        {"text":"Revoke stops a link at once (the answers stay).","url":null}
     ]'),
    ('client_profile', N'Set up a client profile', N'Admin, Merchandiser, Designer',
     N'One profile per brand: the rules its packages are checked against, and its pitch dates.',
     N'[
        {"text":"Open Style Packages > Client profiles.","url":"/packages/clients"},
        {"text":"Click Set up profile (or Edit profile) on the brand.","url":null},
        {"text":"Fill in the rules: minimum recycled %, restricted materials and substances (comma separated, e.g. PVC, PFAS), preferred mills; and the FOB price band, pitch dates, LT account manager and notes.","url":null},
        {"text":"Save. Package pages and What to offer now point out the styles that break the rules; the account manager gets pitch-date reminders in the bell.","url":null}
     ]'),
    ('what_to_offer', N'Find what to offer a client', N'Everyone; adding to a package: Admin, Merchandiser, Designer',
     N'Suggestions from the library alone (rules, no AI), learning from what this client liked or dropped before.',
     N'[
        {"text":"Open Style Packages > What to offer and choose the customer.","url":"/packages/offer?customer={customer}"},
        {"text":"Go through the three groups: Re-offer their styles, Garments they rarely buy, On fabrics they already buy.","url":null},
        {"text":"Keep Hide styles that break them on to follow the client profile''s rules.","url":null},
        {"text":"Tick the styles you want and click Add to package.","url":null}
     ]'),
    ('create_concept', N'Work up a concept (LT design idea)', N'Admin, Merchandiser, Designer',
     N'A concept is LT''s own design idea: pictures, the library styles it takes from, and a story for the client.',
     N'[
        {"text":"Open Style Packages > Concepts and click New concept: name, customer (optional), season, product category, trend tags, gender, target price and the idea.","url":"/concepts"},
        {"text":"Add pictures (mood pictures or sketches). The brand closeness check flags any that look too close to another brand''s design.","url":null},
        {"text":"Add inspiration styles from the library (type a style number, or take one from Proven styles in the library) with a note on what each gives.","url":null},
        {"text":"Write the Story for the client and tick Show to the client on up to 4 pictures.","url":null},
        {"text":"Set the status to Ready, then Offer in a package, or Develop as style to make it an LT design (needs a customer).","url":null}
     ]'),
    ('reuse_style', N'Reuse a style in another season', N'Admin, Merchandiser, Designer',
     N'Reuse copies a style with its colourways, BOM and pictures, and records the carry-over in its history.',
     N'[
        {"text":"Find the style on the Styles list and open it.","url":"/styles?customer={customer}"},
        {"text":"Click Reuse in another season and choose the new season, or make it a variant.","url":null},
        {"text":"Check the copy and change what is new; the History tab shows Changes from the style it was reused from.","url":null}
     ]'),
    ('new_style', N'Add a style', N'Admin, Merchandiser, Designer',
     N'For one style. Many styles at once come from the Excel import (ask me "how do I import styles?").',
     N'[
        {"text":"Open Style Library > Styles and click New style.","url":"/styles"},
        {"text":"Fill in the header: style number, customer, season, product type, gender, weave, model and description. New codes you type are added to their lists.","url":null},
        {"text":"Add colourways on the Colorways tab and BOM lines on the BOM tab (Suggest BOM proposes lines from similar styles).","url":null},
        {"text":"Upload the sketch and photo, or drop the files on the image boxes.","url":null}
     ]'),
    ('compare_styles', N'Compare two styles', N'Everyone',
     N'What changed between a style and an earlier one, worked out by rules (no AI needed).',
     N'[
        {"text":"Open the later style and go to its History tab.","url":"/styles"},
        {"text":"Click Changes from (the style it was reused from) or All changes since; or tick any two styles of the family and click Compare selected.","url":null},
        {"text":"On the compare page, pick another style to compare with, or Swap the two.","url":null},
        {"text":"Or ask me directly, e.g. \u201ccompare S2808MR0000 with F2308MR1600\u201d.","url":null}
     ]'),
    ('import_styles', N'Import styles from Excel', N'Admin',
     N'Loads the Style Library workbook (sheets Style Header, Article, BOM Detail). Nothing reaches the library until Commit.',
     N'[
        {"text":"Open Settings > Import (download the template there if you need one).","url":"/settings/import"},
        {"text":"Choose the workbook and map its sheets and columns; Save this mapping as keeps it for the next file.","url":null},
        {"text":"Click Upload: every row is staged and checked.","url":null},
        {"text":"Check the Preview tabs: Styles (New, Changed, Unchanged, Blocked), Issues, and New codes (Use the suggested spelling where it fits).","url":null},
        {"text":"Click Commit to write the new styles and the changed ones you ticked. Pictures are loaded in Settings > Import pictures.","url":"/settings/image-import"}
     ]'),
    ('bom_check', N'Check BOMs before costing', N'Everyone',
     N'Rules flag BOM lines to look at: missing consumption, LCO vs brand gaps, unusual fabric use.',
     N'[
        {"text":"Open AI Studio > BOM check.","url":"/ai/bom-check"},
        {"text":"Choose one style, or a customer and season (or nothing, for the whole library), and click Run check.","url":null},
        {"text":"Go through the flags, highest severity first, and open each style to fix its BOM.","url":null},
        {"text":"Or ask me, e.g. \u201cBOM problems for ADI 2027-SS\u201d.","url":null}
     ]'),
    ('image_search', N'Find styles by a picture', N'Everyone',
     N'Finds library styles that look like a photo, a description or another style. Everything runs inside LT.',
     N'[
        {"text":"Open AI Studio > Image search.","url":"/ai/image-search"},
        {"text":"Upload a photo, describe the look in words, or pick a library style.","url":null},
        {"text":"Open a style found, or add them to a package.","url":null},
        {"text":"Or ask me, e.g. \u201cstyles that look like a navy hooded jacket\u201d.","url":null}
     ]')
) v (Code, Title, Who, Intro, Steps)
WHERE NOT EXISTS (SELECT 1 FROM ai.Guide g WHERE g.Code = v.Code);

PRINT CONCAT('Library assistant how-to guides: ', @@ROWCOUNT, ' added.');
GO

-- Designer role (2026-11) edits like Merchandiser: seeded guides not changed by an Admin say so too.
UPDATE ai.Guide SET Who = REPLACE(Who, N'Admin, Merchandiser', N'Admin, Merchandiser, Designer')
WHERE ChangedHow = 'Seed' AND Who LIKE N'%Admin, Merchandiser%' AND Who NOT LIKE N'%Designer%';
GO
