# Outside audit 11-09 (docs/Chat audit 11-9.md): what we do with it

Supervisor triage, 2026-11-09. Zach: individual billboards (octahedral if possible) before any cluster/HLOD work.

| Audit item | Decision | Where |
|---|---|---|
| 1. Clouds: profile half/quarter; adaptive light samples; ghosting | Laptop half/quarter already measured (jobs 38, 40: sky 4.5 / 3.5 ms; DrawSkybox 3.26 ms full-res). Do: cut the full-res skybox draw, adaptive light samples, ghosting check | R-8 |
| 2. Unify policies around DetailPolicy; projected bounds for long bodies | Keep. Projected bounds later, only for colossi/long creatures if a shot shows a wrong pick | later |
| 3. Shadows: longer cascades at High/Ultra; wide map texel size | Measure first | job 43 |
| 4. Lighting: cost of the 16 glow slots | Measure first | job 43 |
| 5. Water: SSR/refraction at 10/50/90% coverage; buoyancy interface | Measure first. Buoyancy: when boats/floating items need it | job 43 |
| 6A/B. Hierarchical culling, region HLOD | After R-7's per-instance impostors, and only if the 300 m altitude numbers need it | R-7 scope note |
| 6C. Keep big silhouettes far, cut tiny detail | Already the R-6 rule (trees reach far, shrubs and ground don't) | done |
| 6D. Screen-size terrain LOD with geomorph | Later; no measured problem yet | later |
| 7. Shared background-work scheduler | Find the 200-590 ms streaming spikes first; design note only if they come from contention | W-129 |
| 8. SSAO, overdraw, buffer memory, material variants | SSAO in job 43; the rest when a scene shows a problem | job 43 / later |
| 9. FrameBudget (adaptive quality from measured frame time) | Not now: needs the measurements above and risks visible quality swings. Revisit after R-8, job 43 | later |
| 10. Benchmark suite | The bench runner and spots exist; job 43 adds water/SSAO/glow/shadow rows | job 43 |
