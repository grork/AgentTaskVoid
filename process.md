Reloaded at the beginning of each session. Files are the system of record, and nothing counts as happening, been descided, completed etc unless it's captured in these documents, in this format.

# Files
- `brief.md`: The opening context and motivation for this project. Frozen unless explicitly told otherwise.
- `questions/<topic>/`: One folder per topic. Each question is its own file, named `<ID>-<slug>.md` (slug derived
  from the title). `<topic>/README.md` holds the topic description/framing and an index of its questions (ID,
  title, link, status) so a session can scan topic scope without opening every file. A brand-new topic may start
  as a single `questions/topic.md` file; promote it to a folder (one file per question + README index) once it
  has grown past a couple of questions.
- `requirements.md`: Emergent must-haves (requirements, not questions) discovered mid-session. Append-only, not frozen.

# Question structure
- Each Question has a title
- Each question is assigned a *unique* ID
- Each question has an assigned state (OPEN, BLOCKED(ID-OF-Q-BLOCKED-ON), NEEDS-EXPANSION, DECIDED, DEFERRED, EXPANDED)
- Questions persist forever, and have a terminal status of DECIDED or DEFERRED or EXPANDED
- OPEN questions are ones that are seeking answers
- NEEDS-EXPANSION are questions that need to be expanded into answerable questions
- DECIDED questions include the details on what the outcome was. They also have a one line summary of what the decision was
- DEFERRED questions include a details on why it was deferred
- BLOCKED questions are those that require other questions (which should be listed) to be answered before it can be answered
- EXPANDED questions are answered by being decomposed into other questions. The status of the children has no bearing on the state of the EXPANDED question -- it's still a terminal state.
- EXPANDED questions should point to the initial set of questions it is expanded to.
- Questions can refer to other questions by their ID

# Discovery Sessions
- Work only on NEEDS-EXPANSION questions
- DO NOT DECIDE ANYTHING, ANSWER NOTHING
- Create new questions as they become apparent, each reviewed with the operator for clarity and relevancy
- Ensure new questions are added in 'OPEN' or 'NEEDS-EXPANSION' state -- answering them is a separate phase
- New questions should have new, unique IDs
- New questions should be placed in appropriate topic files or folders. All new topics start out as files. Folder promotion happens separately.

# Answer Sessions
- Work on OPEN questions only
- When a question has been resolved, update it's status with DECIDED (for answered questions) + one line summary; DEFERRED + rational when deferred
- Questions that spawn more questions can be appended -- new ID + details, in the appropriate topic file or folder (if topics is a folder)
- If the question is too complex, mark as NEEDS-EXPANSION for handling during a discovery session
- DEFERRED should be used when the boundary of scope is found making it clear what is or isn't applicable to our current project scope. Check scope before answering, and if not in scope, defer with reasoning.

# Planning Sessions
- The execution plan (`plan/`) is built by consuming DECIDED questions into implementation phases. A phase can only be planned once the questions it depends on are DECIDED (or explicitly DEFERRED / out of scope).
- Every DECIDED question carries a `**Plan:**` line (immediately under `**Status:**`) recording whether it has been consumed into the plan yet:
  - `**Plan:** phase-NN` -- consumed into that phase. Permanent: it stays even when later questions revisit the same surface. That later work is a NEW question with its own future phase, not a re-opening of this one (e.g. a v2 verb contract does not un-plan the v1 phase that shipped).
  - `**Plan:** all-phases` -- consumed as a standing invariant (the list in `plan/README.md`) that applies to every phase, rather than being delivered by a single one.
  - `**Plan:** unplanned` -- DECIDED but not yet turned into plan work. These are the queue a planning session works from.
- DEFERRED, EXPANDED, OPEN, and BLOCKED questions carry NO `**Plan:**` line. DEFERRED is out of scope; EXPANDED is decomposed into children that carry their own disposition; OPEN/BLOCKED are not yet decided, so there is nothing to plan.
- To extend the plan, a planning session plans exactly the DECIDED questions stamped `unplanned`: it writes the new phase file(s) and re-stamps those questions `phase-NN`. Questions already stamped `phase-NN` / `all-phases` are done -- never re-plan them. That stamp is how a session discerns, without re-deriving it, which decisions are already in the plan.
- A DECIDED question with NO `**Plan:**` line is a bug (an un-triaged decision), not a state -- stamp it.
- Consumption is not build status. Whether a planned phase has actually been *implemented* is tracked separately in `progress.md`; the `**Plan:**` stamp only records that a decision was folded into the plan.

# Completion
When there are no OPEN or NEEDS-EXPANSION or BLOCKED questions left, we have reached a full set of information & scope that we need to build this product.