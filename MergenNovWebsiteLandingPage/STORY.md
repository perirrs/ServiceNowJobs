# Follow one ticket: the Mergen scroll story

Open `index.html` through any static server (for example `python -m http.server 4173` in this folder) and scroll.

## The idea

United Carriers tells its story by following one container: a crane lifts it, a truck carries it, a ship takes it to sea. The visitor never reads "we do end-to-end logistics". They watch it happen to one object.

Mergen's equivalent of the container is **one incident, INC0048213**, and its equivalent of the road is **one thread**. The thread starts as a knot, because that is what work looks like in a tangled IT estate. Each chapter does something to the thread, and each thing is a stage of a Mergen engagement:

| # | Chapter | What the thread does | What Mergen is doing | Lifecycle |
|---|---------|----------------------|----------------------|-----------|
| 00 | Opening statement | 130 ticket cards scattered, each tied to its source: email, portal, phone, chat, monitoring, walk-up | Hero: partner status, what Mergen does, four numbers, then the incident | |
| 01 | The backlog | One ticket zigzags between nine queues, drawing its path as it goes. The crowd reddens with age | The customer's problem, shown and costed | |
| 02 | The call | The crowd dims. Rings go out | The customer contacts Mergen | Advise |
| 03 | Listen | The crowd sorts itself into six columns by where each ticket is stuck, with counts | Discovery: shadowing, interviews, instance scan | Advise |
| 04 | Measure | Pulls straight into a 54-hour timeline. A scan names five gaps | Gap analysis and maturity assessment | Advise |
| 05 | Propose | Redraws as a dashed five-stop route on a blueprint grid | Roadmap, business case, design rules, operating model | Advise |
| 06 | Build | Solidifies stop by stop. Each stop gets its ServiceNow module | Sprint delivery, testing, cutover | Build |
| 07 | Go live | The same ticket runs the route in 12 minutes | The outcome, against the baseline | Build |
| 08 | Stay | Closes into an Advise / Build / Run loop the ticket keeps circling | Hypercare, support, upgrades, handover | Run |
| 09 | The return | Becomes a rising 36-month savings curve | The money: a calculator on the visitor's own numbers | |

After the story: proof (delivery numbers, one case study, four principles), industries, and one call to action, "Bring us your worst ticket."

## The scroll mechanics

- **One fixed stage.** A full-screen canvas sits behind the page. Chapters are ordinary sections that scroll over it, so all the copy is real, selectable, indexable text.
- **Scroll decides the shape.** Each chapter owns a shape (knot, line, route, loop). While a chapter is on screen its progress drives the detail: the ticket's position, the scan line, how much of the route is built. During the last screen-height of a chapter the thread morphs into the next chapter's shape.
- **The record bar keeps score.** The strip along the bottom is the incident's record header: state, assignment group, elapsed time, reassignment count, hours lost. It runs live through chapter 01, in step with the timestamps in the copy, freezes as the "as-is baseline" while Mergen works, then runs again in chapter 07 on the new route. This is the page's version of the United Carriers speedometer.
- **Phones.** The thread plays in the top third of the screen and the copy reads on cards below it. The pinned labels are dropped, and the record bar carries the numbers.
- **Reduced motion.** With the system setting on, the thread stops rotating, scroll smoothing is off, and all copy is visible without animation.

## Where the numbers come from

- **The incident is illustrative**, and the footer says so. 340 users and 54 h 08 m (Mon 09:02 to Wed 15:10) give roughly 23 working hours each, or 7,820 hours. The 12-minute version gives 68.
- **The savings figure is the visitor's own arithmetic.** Defaults give $4.32M a year. The four working assumptions (30% deflection, 35% less effort, 40% less major-incident time, $120K per retired tool) are printed under the calculator and should be replaced with figures Mergen is comfortable defending.
- **Mergen figures** (500+ programmes, 70+ assessments, 120+ modules, 75+ custom applications, and the wealth-manager case study) are taken from the current site at thedemoproject.com/mergen_react_infinity.
- **Third-party benchmarks** are attributed on the page: New Relic's 2025 Observability Forecast ($2M an hour median, up to $76M a year), ServiceNow's own "Now on Now" service desk story (75 minutes to under 10), and ServiceNow's Wellstar customer story (25 minutes to 9).

## Files

- `index.html`: all copy and structure
- `styles.css`: layout, type, the light closing sections
- `story.js`: the stage. Chapter states are in `state()`, the pinned labels in `PINS`, the savings maths in `calc()`
