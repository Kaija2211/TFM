# TFM — AI Project Context

This file is intended as stable project context for AI assistants working on the repository. It records where TFM came from, the submitted MSc research artefact that established its simulation foundation, and the current direction as a football-management game.

Last updated: 2026-08-14
Student: Thomas Kaija Bernards — 100475
Programme: MSc Professional Practice in Games Programming, SAE Institute London
Working project title: **The Art of the Trade-off: Balancing Realism and Performance in Large-Scale Football Simulations**
Possible prototype/game-facing title under discussion: **Beyond the Scoreline**, **Touchline**, **The Technical Area**, **Match Engine**, or **Season Lab**.

## Current project status (post-submission)

Thomas submitted TFM v0.1 and the dissertation on 2026-08-14. The MSc research phase is complete. The `unity6-ai-prototype` branch preserves that submitted prototype lineage; `main` is now the unrestricted game-development branch.

The research material below is historical and architectural context, not an active development constraint. There are no protected or untouchable source files anymore. AI assistants may modify, consolidate, replace, or remove code across `Manager`, `ManagerSim`, `Sim`, `Data`, Research Mode, evaluation tooling, shared models, and project settings when doing so serves the current game.

Current priority: make TFM an actual, maintainable, enjoyable football-management game. Prefer sound architecture, player-facing quality, testing, and long-term maintainability over preserving dissertation-era boundaries.

---

## 1. Core project identity

This project investigates the trade-off between **simulation realism** and **computational scalability** in large-scale football management simulations.

The central research question from the Major Project proposal is:

> Which data-driven simulation paradigm, Agent-Based Model or Statistical Model, provides the optimal balance of realism and scalability for a non-graphical, large-scale football management simulation?

The project compares two football simulation paradigms:

1. **Statistical Model (SM)**
   - Uses historical football results to estimate team strengths.
   - Produces match outcomes probabilistically.
   - Prioritises speed, scalability, and repeated large-scale season simulation.

2. **Agent-Based Model (ABM)**
   - Simulates player agents with attributes and simplified behavioural rules.
   - Produces event-level match narratives and richer internal match flow.
   - Prioritises perceived realism, explainability, and game-facing detail, but costs more compute.

The project should not frame either model as universally “better.” The intended thesis is about **trade-offs**: whether increased behavioural detail is worth the performance cost compared with a simpler statistical approach.

---

## 2. Course and assessment context

The project belongs to SAE's MA/MSc Professional Practice in Creative Media Industries structure. The programme is project-based and focuses on technical skill development, theoretical/practical integration, independent problem solving, critical analysis, reflection, and professional practice.

The full-time structure is:

1. Project Planning & Theoretical Principles
2. Professional Development Scheme
3. Creative Media Major Project

The programme guide emphasises:

- developing professional practice;
- enhancing technical skills in a specialist area;
- understanding links between theory and practical work;
- solving complex problems independently;
- developing critical, analytical, and reflective skills;
- communicating regularly with the Learning Advisor;
- engaging with project design, development, and production;
- managing work through online/VLE-supported study;
- holistic assessment based on portfolio, milestones, project management, reflection, transferable skills, and discipline-specific production.

Historical note: during the MSc phase this was both software and an assessed artefact. That submission is now complete; future suggestions should focus on making the game better rather than preserving assessment constraints.

---

## 3. Personal/professional development framing

A major reason for using Unity and C# is professional growth. During undergraduate study, Thomas worked mostly in Unreal Engine and relied heavily on Blueprint scripting. The MSc project deliberately moved into Unity/C# to develop stronger programming, data handling, debugging, and performance-aware system design skills.

This matters for the write-up. The artefact is not just a game prototype; it is evidence of a shift from visual-scripting comfort into a more code-driven workflow.

Relevant professional development themes:

- Unity/C# skill growth;
- Git/version control workflow;
- data parsing and caching;
- simulation architecture;
- quantitative evaluation;
- performance-aware development;
- reflective project management;
- scope control after feature creep/burnout risk.

---

## 4. Previous module foundation: Professional Development Scheme

The Professional Development Scheme project was scoped as a technical foundation for the Major Project rather than a complete simulation.

Its intended practical outputs were:

- Unity prototype project files;
- C# source code files;
- prototype documentation/README;
- evidence screenshots;
- Gantt chart;
- reflective report;
- learning journal.

The project plan allocated approximately **375 practical study hours** and **1500 written words** for the reflective report. The planned technical focus was:

- Unity/C# project setup and workflow development;
- Git/version control and technical pipeline documentation;
- football-data API integration and local caching;
- JSON inspection and parsing;
- internal match record generation;
- basic league table prototype and simulation scaffold.

This earlier module established the data-pipeline mindset: reproducibility, cached data, parsing, internal match records, and transforming raw football data into a league-table output.

---

## 5. Dataset and implementation direction

The original proposal planned to train on Premier League seasons 2017/18 to 2023/24 and evaluate on 2024/25. During implementation, this was revised to use broader available data:

- training: Premier League seasons 2017/18 to 2024/25;
- evaluation/holdout: 2025/26 season.

This revision should be explained transparently in the dissertation as a practical implementation change that increased training coverage while preserving an unseen evaluation season.

The project initially considered/used football-data.org API access, but later moved toward local OpenFootball-style season text files for reproducibility and to avoid API access limitations. The PDS plan already identified dataset access restrictions and local caching as important risk-mitigation concerns, so this pivot is academically defensible.

Historical dataset principles used for the submitted evaluation:

- Do not mix training and evaluation data.
- Preserve an unseen holdout season for evaluation.
- Keep source-data parsing reproducible.
- Document any pivot away from the original API plan.
- Treat matchday metadata carefully; do not infer matchdays by file position if explicit matchday markers exist.

---

## 6. Research Mode vs Manager Mode (historical architecture)

The project now has two conceptually separate modes:

### Research Mode

Purpose: controlled evaluation of SM vs ABM.

Research Mode is used for:

- training models;
- simulating seasons repeatedly;
- calculating predictive accuracy;
- measuring execution time and simulations per minute;
- exporting evaluation evidence;
- supporting the dissertation's quantitative findings.

Research Mode was kept clean and reproducible for the submitted comparison. It may now be changed, repurposed, integrated, or retired if that benefits the game or its development tooling.

### Manager Mode

Purpose: game-facing prototype demonstration.

Manager Mode demonstrates how the simulation systems could support a football management game loop. It is not the controlled research evaluation instrument.

Current Manager Mode features include:

- choose-your-club screen covering all 20 clubs from fixture data;
- season hub;
- tactics chosen between matches, not mid-match;
- full matchday simulation using parsed real matchday metadata;
- full 20-team league table progression;
- team-strength-based squad generation;
- position-weighted Overall ratings;
- view squad screen;
- player inspect screen;
- matchday preparation;
- substitutions work in progress;
- text-based match replay and full-time statistics;
- UI design direction based on a dark navy/green football-management concept.

Manager Mode became the foundation of the actual game. The former requirement to isolate it from Research Mode no longer applies.

### The ManagerSim fork (added 2026-08-09)

Manager Mode has its own duplicate of the match simulator at `Assets/Scripts/ManagerSim/AgentMatchSimulator.cs`. It started as a byte-for-byte copy of `Assets/Scripts/Sim/AgentMatchSimulator.cs` so the submitted research comparison could remain stable while game-facing match logic evolved.

The fork is declared in `namespace Manager` with the same class/type names as the original. `ManagerPrototypeController.cs` therefore resolves the Manager fork, while `ResearchEvaluationRunner.cs` resolves `Sim.AgentMatchSimulator`. This is useful architectural history, not a boundary that must be preserved.

Future work may edit either simulator, merge them behind shared components/interfaces, replace them, or remove obsolete Research Mode duplication. Choose the architecture that best supports the game; no file is protected.

---

## 7. Current evaluation framing

The project uses a mixed-methods approach, but the study design must be honest about what each model actually outputs.

### Quantitative evaluation

Both models can be compared directly on:

- final league table accuracy;
- points Mean Absolute Error (MAE);
- execution time;
- simulations per minute;
- possibly CPU utilisation if captured;
- title-winner/relegation plausibility where relevant.

The strongest current interpretation is:

- the Statistical Model is vastly faster and slightly more accurate on repeated season outcomes;
- the Agent-Based Model produces comparable table accuracy while also generating event-level match narratives;
- therefore ABM realism/game-facing richness comes with a performance cost.

Recent confirmed output figures from implementation discussions:

- Statistical repeated evaluation over 100 runs:
  - Average Points MAE: 11.59
  - Best Points MAE: 8.05
  - Worst Points MAE: 16.05
  - Execution time: 0.0484 seconds
  - Simulations per minute: 123,920.86

- Agent-Based repeated evaluation over 100 runs:
  - Average Points MAE: 11.89
  - Median Points MAE: 11.68
  - MAE standard deviation: 1.55
  - Best Points MAE: 8.15
  - Worst Points MAE: 15.10
  - Execution time: 4.1958 seconds
  - Simulations per minute: 1,430.01
  - Title winners included Manchester City, Liverpool, Arsenal, etc.

These figures should be checked against the final exported evidence files before submission.

### Qualitative evaluation / user study

The original proposal planned a blinded user study using Likert-scale questions and open-ended feedback to evaluate perceived realism.

Important methodological refinement:

Do not create fake Statistical Model event narratives just to make it look equal to ABM. That would make the comparison dishonest.

Better structure:

1. **Outcome plausibility comparison**
   - Compare SM and ABM outputs using things both models produce: scorelines, tables, points totals, title/relegation outcomes.
   - This can be blinded fairly.

2. **ABM narrative believability assessment**
   - Separately assess whether ABM event feeds feel believable and useful for a football management game.
   - This should not pretend SM has an equivalent event narrative.

Suggested dissertation framing:

> The models can be compared directly on what they both output, but the ABM must also be evaluated on what only it outputs: event-level simulation.

---

## 8. Proposed dissertation structure

The Major Project proposal's intended structure was:

1. Introduction
2. Literature Review
3. Simulation Paradigms in Football Games
4. Methodology
5. Implementation
6. Results
7. Discussion
8. Conclusion and Future Work

Working written-portion target discussed for the Major Project: approximately **6000 words**, with the practical artefact carrying significant project weight. There was earlier broader discussion around 6000–9000 words, but the working target became roughly 6000 written words.

A practical breakdown could be:

- Introduction: 600–800
- Literature Review / Context: 1200–1600
- Methodology: 900–1200
- Implementation: 1200–1600
- Results: 800–1000
- Discussion: 900–1200
- Conclusion/Future Work: 400–600

This should be adjusted to final assessment instructions if a stricter word count exists.

---

## 9. Key literature and theory themes from the proposal

The proposal uses/mentions literature around:

- football match modelling;
- stochastic/Poisson football models;
- agent-based simulation;
- long-term football outcome optimisation;
- optimisation in sports strategy;
- game optimisation and performance trade-offs;
- user-study bias and experimenter effects;
- Likert-style usability/perception questionnaires.

Important conceptual anchors:

- Statistical models are efficient and scalable but abstract.
- Agent-based models can offer richer behavioural/narrative detail but are computationally heavier.
- Football management games are CPU-bound because they simulate many matches and long-term league states rather than relying mainly on graphics.
- Realism should be split into objective outcome similarity and perceived believability.
- The goal is evidence-based trade-off analysis, not declaring a universal winner.

---

## 10. Ethics and participant considerations

The proposal planned adult participants only, likely football-management/football-simulation players. The qualitative evaluation should use anonymised responses and avoid collecting unnecessary personal data.

Bias-control ideas from the proposal include:

- blinded model labels;
- comparable presentation formats where possible;
- counterbalancing when showing Model A / Model B;
- Likert-scale questions plus open-ended feedback;
- avoiding cues that reveal which model produced which output.

Updated caution:

- Do not blind a fake SM narrative against a real ABM event feed.
- For direct comparison, compare only shared output types.
- For ABM-only event feedback, be explicit that the question is narrative believability/game suitability, not a direct SM-vs-ABM narrative comparison.

---

## 11. AI-use transparency

Generative AI has been used as development support for:

- planning;
- debugging;
- refactoring;
- UI scaffolding;
- code review style discussions;
- implementation assistance through Claude/Unity tooling.

The final submission should be transparent that AI suggestions were reviewed, tested, and accepted/rejected by Thomas. The research question, methodology, final interpretation, testing decisions, and submission remain Thomas's responsibility.

Suggested wording:

> Generative AI tools were used as development support during implementation, particularly for code refactoring, debugging, and UI scaffolding. AI-generated suggestions were reviewed, tested, and modified by the author before inclusion. The project design, research question, methodology, evaluation criteria, interpretation of results, and final submission remained the author's own work.

---

## 12. Current development principles for AI assistants

The dissertation-era restrictions are retired. When working on this repo:

1. **No source file or subsystem is untouchable.** Modify shared `Sim`, `Data`, Manager Mode, Research Mode, evaluation code, project settings, or assets when the task calls for it.
2. **Architecture may cross the old mode boundary.** Reuse, merge, refactor, or replace dissertation-era forks and duplicated systems when that improves maintainability.
3. **Research metrics are no longer approval gates.** They remain useful historical benchmarks, not immutable product requirements.
4. **Parallelism is allowed.** Threads, tasks, jobs, Burst, ECS, async workflows, or other performance approaches may be considered when technically appropriate and safely implemented.
5. **Preserve working states before risky changes.** Respect the current branch/worktree, existing user changes, and version-control safety.
6. **After changes, summarise what changed and how it was verified.**
7. **Prefer focused, testable work, but broad refactors are allowed** when the architecture genuinely benefits and verification is proportionate.
8. **Protect “the holy balance” through testing, not file restrictions.** Changes to scoring, shots, saves, player growth, transfers, tactics, condition, injuries, finances, league outcomes, team strength, or fixture simulation should be validated against relevant distributions and player-facing expectations. See the protocol below.
9. **Treat historical research code honestly.** Do not rewrite or relabel old evidence as if it came from the submitted experiment, but feel free to improve or replace the runtime systems used by the game.
10. **Maintain useful project history.** Continue using `DEVLOG.md` and `HANDOFF.md` when they add value, but they are development tools now rather than submission requirements.

Current rule:

> The submitted MSc artefact is preserved in Git history and on `unity6-ai-prototype`; development on `main` is free to evolve TFM into the strongest game it can become.

### The holy balance

Thomas refers to the established statistical plausibility of TFM’s football world as **“the holy balance.”** Major changes are allowed to alter simulation code, but they must not casually or silently wreck scorelines, goals per match, goal differences, points totals, or league-table shape.

Treat a change as holy-balance-sensitive if it can directly or indirectly affect:

- scoring, shooting, saving, chance creation, defending, or match-event probabilities;
- player attributes, Overall, Potential, aging, development, fatigue, condition, injuries, or position fit;
- tactics, formations, substitutions, mentality, squad selection, or team-strength calculations;
- transfers, retirement replacement, squad generation, finances, or AI squad depth;
- fixture processing, season rollover, league-table updates, or simulation randomness.

For a significant balance-sensitive change, verification should normally include:

1. Simulate multiple complete 20-team seasons—preferably at least five, with all 380 fixtures applied per season.
2. Record league-wide goals per match, both-teams-to-score rate, scoreless-draw rate, and the scoreline distribution where practical.
3. Record champion and bottom-club points, overall points range, goals-for/goals-against range, and goal-difference range.
4. Inspect representative strong, middle, and weak clubs for plausible results and long-term team-strength drift.
5. Check for pathological outcomes: runaway 100+ point seasons becoming routine, implausibly compressed tables, extreme GD inflation, widespread scoreless matches, excessive goal totals, or clubs structurally collapsing because of missing players.
6. Compare against the previous known-good build or a baseline run using enough repeated simulation to distinguish a real regression from random variance.

Historical Manager Mode checks have produced roughly **2.8–2.9 total goals per match** across full seasons, with plausible 20-team points and GD spreads. This is a reference neighbourhood, not an immutable target: intentional design changes may move it, but substantial movement should be understood, documented, and accepted rather than discovered accidentally in a later playthrough.

Do not consider a balance-sensitive change verified merely because it compiles or one match looked reasonable. Temporary test hooks or editor scripts are acceptable for bulk simulation, but remove them after verification unless they are deliberately promoted into maintained regression tooling.

---

## 13. Submitted evidence archive

The following artefacts remain useful historical records of the completed MSc submission. Do not falsify or misrepresent them, but they no longer constrain runtime development:

- final Statistical Model repeated summary text file;
- final ABM repeated summary text file;
- ABM average table CSV;
- screenshots of Research Mode output/console;
- screenshots of Manager Mode title/new career/hub/squad/player/matchday screens;
- screenshots or notes showing full league table progression;
- Git commit history demonstrating iteration;
- `DEVLOG.md` (repo root) - dated development journal, problems/fixes per session;
- AI-use log or summary;
- user-study materials and anonymised results if completed.

---

## 14. Current implementation/design status from project conversation

The latest Manager Mode concept has evolved substantially beyond the original console output:

- tactics moved to the hub between matches;
- 20-club selection before career start;
- full division table through real parsed matchday metadata;
- team-strength-based squad generation;
- position-weighted Overall ratings;
- squad list and player-detail screens;
- matchday prep screen;
- dark navy/green UI concept influenced by the uploaded `gameui.pdf` design;
- substitutions under active development in a newer Claude chat.

This was described in the submission as a **prototype extension** or **game-facing demonstration**. Post-submission, it is the starting point for the actual game.

---

## 15. Suggested first prompt for a new AI coding chat

Use this prompt when starting a new Claude/AI coding chat:

> You are helping develop TFM, a Unity/C# football-management game that grew out of Thomas Bernards' completed MSc simulation project. Read `PROJECT_CONTEXT_FOR_AI.md` first. The submitted research prototype is preserved on `unity6-ai-prototype`; development on `main` has no protected files or dissertation-era architecture restrictions. Improve any subsystem needed for the game, preserve unrelated user work, test proportionately, and summarise files changed and verification performed. Current focus: [insert exact task].

---

## 16. Important final interpretation

The likely final thesis argument is:

> The Statistical Model achieved slightly better repeated outcome accuracy and vastly higher throughput, making it preferable when scalability is the dominant requirement. The Agent-Based Model produced comparable outcome accuracy while enabling event-level narrative output and a richer manager-facing prototype, making it more suitable where perceived match realism and interpretability are more important. The optimal paradigm therefore depends on the design priority: speed and scale versus behavioural detail and game-facing believability.

