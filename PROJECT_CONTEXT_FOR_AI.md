# Master's Work — AI Handoff Context

This file is intended as a stable project-context document for AI assistants working on the repository. It summarises the MSc project, course context, research framing, implementation direction, evaluation plan, and current project boundaries without relying on source-code details.

Last updated: 2026-08-06
Student: Thomas Kaija Bernards — 100475
Programme: MSc Professional Practice in Games Programming, SAE Institute London
Working project title: **The Art of the Trade-off: Balancing Realism and Performance in Large-Scale Football Simulations**
Possible prototype/game-facing title under discussion: **Beyond the Scoreline**, **Touchline**, **The Technical Area**, **Match Engine**, or **Season Lab**.

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

For AI assistants: this is not only a software project. It is an MSc artefact plus written academic submission. Suggestions should support evidence, reflection, professional development, and academic defensibility, not just feature growth.

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

Important dataset principles:

- Do not mix training and evaluation data.
- Preserve an unseen holdout season for evaluation.
- Keep source-data parsing reproducible.
- Document any pivot away from the original API plan.
- Treat matchday metadata carefully; do not infer matchdays by file position if explicit matchday markers exist.

---

## 6. Research Mode vs Manager Mode

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

Research Mode should remain clean, reproducible, and isolated from game-facing features.

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

Manager Mode is allowed to be more game-like and user-facing, but it must not contaminate Research Mode metrics or alter research evaluation behaviour.

### The ManagerSim fork (added 2026-08-09)

Manager Mode now has its own duplicate of the match simulator, free to edit: `Assets/Scripts/ManagerSim/AgentMatchSimulator.cs`. It started as a byte-for-byte copy of `Assets/Scripts/Sim/AgentMatchSimulator.cs` and is free to diverge from there - it exists specifically so Manager Mode can get real match-resolution changes (its first use: a genuine on/off-target shot distinction, for an honest Shots on Target stat) without ever touching the protected original.

How it stays safe: the fork is declared `namespace Manager` (not a new namespace) with the exact same class/type names as the original. `ManagerPrototypeController.cs` is itself `namespace Manager` and already had `using Sim;`, so C#'s same-namespace type resolution makes the fork shadow `Sim.AgentMatchSimulator` there automatically - no call sites needed to change. `ResearchEvaluationRunner.cs` (`namespace Data`, no `using Manager;`) is completely unaffected and still resolves to the real, untouched `Sim.AgentMatchSimulator`. This was verified live, not just reasoned about - a temporary marker string proved the fork is genuinely what Manager Mode runs, then removed.

**If a future Manager Mode feature needs a real match-logic change** (not just pre/post-processing around the sim, which `ManagerFormationFit`/`ManagerMentalityModifier` already do without needing a fork): check whether the ManagerSim fork already covers it before assuming the protected original needs touching - it usually doesn't need to. The same fork-by-namespace-shadowing pattern could apply to some other currently-protected `Sim`/`Data` file later if a concrete need arises; it hasn't been needed yet beyond this one file.

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

## 12. Current guardrails for AI assistants

When working on this repo, follow these constraints:

1. **Do not modify Research Mode unless explicitly asked.**
2. **Keep Manager Mode separate from Research Mode.**
3. **Do not change evaluation metrics silently.**
4. **Do not change the training/evaluation split without explicit approval.**
5. **Do not invent fake Statistical Model event narratives.**
6. **Do not add features before preserving/committing working states.**
7. **After changes, summarise files created/modified and how to test.**
8. **Prefer small, testable tickets over broad refactors.**
9. **If implementation would affect research numbers, stop and ask.**
10. **Preserve academic honesty over making the prototype look more impressive.**
11. **Multithreading/parallelism is out of scope.** Do not suggest or implement it (e.g. `Task.Run`, `Thread`, `Parallel.For`) as a performance fix for Research Mode or Manager Mode — this is a deliberate scope-control decision, and it keeps the recorded SM-vs-ABM execution time / sims-per-minute comparison on a consistent single-threaded basis.
12. **"Don't touch the match simulator" means the original, not Manager Mode's fork.** `Assets/Scripts/Sim/AgentMatchSimulator.cs` must stay untouched (guardrail #1 above). `Assets/Scripts/ManagerSim/AgentMatchSimulator.cs` is a deliberate, free-to-edit Manager Mode-only duplicate (see section 6) - editing it is normal Manager Mode work, not a Research Mode violation. Always verify with `git diff` on the `Sim/` original specifically (not just "no errors") before considering a match-logic change to be Research-Mode-safe.

Useful rule:

> Game-facing features may improve Manager Mode, but they must not alter the controlled Research Mode comparison unless Thomas explicitly approves and re-runs evidence.

---

## 13. Evidence to preserve

For dissertation/evidence folders, preserve:

- final Statistical Model repeated summary text file;
- final ABM repeated summary text file;
- ABM average table CSV;
- screenshots of Research Mode output/console;
- screenshots of Manager Mode title/new career/hub/squad/player/matchday screens;
- screenshots or notes showing full league table progression;
- Git commit history demonstrating iteration;
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

This should be described as a **prototype extension** or **game-facing demonstration**, not as the controlled evaluation tool.

---

## 15. Suggested first prompt for a new AI coding chat

Use this prompt when starting a new Claude/AI coding chat:

> You are helping with Thomas Bernards' MSc Games Programming Major Project: a Unity/C# football simulation comparing a Statistical Model and Agent-Based Model for realism/scalability trade-offs. Read `PROJECT_CONTEXT_FOR_AI.md` first. Preserve the separation between Research Mode and Manager Mode. Do not modify Research Mode or evaluation metrics unless explicitly asked. Work in small, testable changes. After every task, summarise files created/modified, how to test, and whether Research Mode is affected. Current focus: [insert exact task].

---

## 16. Important final interpretation

The likely final thesis argument is:

> The Statistical Model achieved slightly better repeated outcome accuracy and vastly higher throughput, making it preferable when scalability is the dominant requirement. The Agent-Based Model produced comparable outcome accuracy while enabling event-level narrative output and a richer manager-facing prototype, making it more suitable where perceived match realism and interpretability are more important. The optimal paradigm therefore depends on the design priority: speed and scale versus behavioural detail and game-facing believability.

