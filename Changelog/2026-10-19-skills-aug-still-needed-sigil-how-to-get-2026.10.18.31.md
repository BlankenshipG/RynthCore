# Engine 2026.10.18.31: Skills panel shows luminance still needed and how to get chain items (2026-10-19)

Previous release: 2026.10.18.30 (automated system text in the System tab). Goes with RynthILT 0.2.23.

## Changed (Skills panel, Progression tab)

- **Augmentations:**
  - A STILL NEEDED row under TOTALS: the luminance still needed after banked and character
    luminance, and the e-coins short. It is green when nothing is left.
  - The line under the table that shows the subtraction.
- **Enlightenment, "Still need / have" and "Sigil craft chain":**
  - A "How to get" column (dim, wrapped) with RynthILT's recipe or vendor trade for each row.
  - With an older RynthILT, the chain keeps its "craft" marker and the materials table keeps three columns.
- **Vendor rates** under the chain: Extracts per Ascension Coin, and the Trophy trade (extracts
  and Trophies). They are sent as `prog extractsperasc` / `trophyextracts` / `trophycount` and
  shown only when RynthILT reports them.
- `ProgressionData`:
  - `AugPlanner`: `stillLum`, `stillCoins`, `stillLine`.
  - `EnlPlanner`: `extractsPerAsc`, `trophyExtracts`, `trophyCount` (-1 when absent).
  - `EnlMaterialRow` and `EnlChainRow`: `how`.
