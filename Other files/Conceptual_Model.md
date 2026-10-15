# 4. Conceptual Model

## 4.1 Purpose and Scope of this Chapter

Before a simulation can be implemented, the system under study must be abstracted
into a *conceptual model*: a non-software-specific description of what is to be
represented and, equally important, what is deliberately left out. Following
Robinson (2008), a conceptual model is understood here as the set of modelling
objectives, model inputs (experimental factors), model outputs (responses), and
model content (its scope and level of detail), together with the assumptions and
simplifications that justify the chosen level of abstraction. The conceptual model
is independent of the programming language, simulation package, or data structures
eventually used; those belong to the implementation described in the following
chapter.

This chapter presents the conceptual model of the Wiener Neustadt flat shunting
yard. Its internal structure adopts the hierarchical control-structure approach of
Furian et al. (2015), in which the operating logic of a discrete-event system is
decomposed into cooperating *control units*, each responsible for a distinct phase
of the process and communicating through a small number of well-defined interfaces.
This decomposition is used throughout the chapter as the organising principle for
the model's control logic.

The simulation is a *discrete-event simulation* (DES): the state of the yard changes
only at discrete points in simulated time, each corresponding to an event such as a
train arriving, an activity commencing, or a resource being released. Between events
the state is constant, so the model advances by jumping from one event to the next
rather than by fixed time steps. In its current form the model is *deterministic*
and *trace-driven*: inbound traffic is supplied as an explicit arrival schedule
rather than sampled from a distribution, and every activity duration is a
deterministic function of the entities and resources involved. The implications of
this choice are discussed under simplifications (Section 4.9) and revisited as an
avenue for future work.

## 4.2 The Real System

Wiener Neustadt is a *flat* classification (shunting) yard. Unlike a hump yard, where
wagons are pushed over a raised hump and roll onto classification tracks under gravity,
a flat yard performs all sorting by locomotive propulsion. Its operation follows a
recurring cycle:

An inbound freight train arrives at the yard and is placed on an *arrival track*. The
train is a sequence of *wagon groups*, each destined for a particular downstream
destination. Yard staff prepare the train for sorting and then *push it off*: the
shunting locomotive propels the train back over a single connecting track (the yard
"neck") and, at a decoupling point, successive same-destination portions of the train
are detached and rolled onto *classification tracks*, one track per destination. On the
classification side, arriving portions are secured or coupled to the wagons already
standing on their track. Once a track has accumulated enough wagons to form a train,
those wagons are declared an *outbound train*, prepared for departure, and driven out
of the yard through the *exit gate* onto the network. The yard's function is therefore
to disassemble arriving trains and reassemble their wagons, by destination, into new
departing trains.

Three physical features shape the model's constraints. First, the arrival and
classification track groups are joined by a single connecting track over which every
push-off must pass; only one push-off can use it at a time. Second, departures leave
through a single exit gate, which serialises outbound movements. Third, the yard's
work is performed by a finite pool of ground staff (shunters) and a finite pool of
shunting locomotives, both of which must physically travel to the location of each
task before work can begin.

## 4.3 Modelling Objectives

The general objective is to provide a decision-support model that quantifies the
performance of the Wiener Neustadt flat shunting yard and exposes where time is
spent and where finite resources constrain throughput. The model is intended as an
analysis platform: a means of measuring the yard's behaviour under a given traffic
and resource configuration, and of exploring the effect of changing that
configuration.

The specific objectives are:

- To decompose the total time each inbound train, wagon group, and outbound train
  spends in the yard into its constituent waiting and processing phases, so that the
  dominant contributors to dwell time can be identified.
- To quantify the utilisation of, and contention for, the yard's finite resources —
  the ground staff, the shunting locomotives, the single connecting (passage) track,
  and the single exit gate.
- To support investigation of specific operational questions raised during the
  project, in particular the causes of long dwell times on the classification tracks
  (for example, whether a wagon group waits chiefly for sibling wagon groups to
  arrive, or for downstream resources such as the exit gate).
- To provide a validated, parameterised model on which scenario experiments —
  varying traffic intensity, staffing, locomotive availability, gate capacity, and
  train-formation thresholds — can subsequently be run.

## 4.4 Model Outputs (Responses)

The model's responses are a set of key performance indicators (KPIs) reported per
entity and in aggregate. They serve two roles, in line with Robinson's (2008)
distinction: first, to *measure* whether the modelling objectives are met (throughput
and total dwell times); and second, to *diagnose* the reasons behind the observed
performance. The diagnostic value comes from separating, for every activity, the time
spent *waiting* for resources from the time spent *performing* the work: a large
"wait" component points to resource contention, whereas a large "duration" component
points to the work content itself.

The KPI set is grouped by the three principal entity types.

**Incoming Trains (per inbound train).** Entry-queue wait; arrival-drive duration; ITP
wait; ITP duration; push-off wait; push-off duration; total time in the arrival yard;
and total time in the system.

**Wagon Groups (per wagon group).** Time in the arrival yard; time in the
classification yard; and idle time on the classification track (the interval between a
wagon group completing its own securing or coupling and the outbound-train preparation
beginning — the period during which it is neither being worked on nor yet part of an
outbound movement).

**Outbound Trains (per outbound train).** Outbound-train-preparation (OBTP) wait; OBTP
duration; exit-gate wait; departure-drive duration; and total time on the
classification track.

These per-entity KPIs are complemented by supporting outputs: aggregated drive
statistics (distance, duration, and derived speed for each arrival, push-off, and
departure movement); a per-activity record of every submitted, started, and completed
activity; and a plain-language reconstruction of each wagon group's life on the
classification track. All duration KPIs are expressed on a common `[h]:mm:ss` basis.
Table 4.1 lists the KPIs and their definitions.

**Table 4.1 — Model outputs (key performance indicators).**

| Entity | KPI | Definition |
|---|---|---|
| Incoming train | Entry-queue wait | Entry to start of arrival drive (waiting for a free arrival track) |
| Incoming train | Arrival-drive duration | Arrival-drive start to arrival on the arrival track |
| Incoming train | ITP wait | ITP submitted to ITP started (worker + shunting-loco allocation and travel) |
| Incoming train | ITP duration | Duration of the incoming-train-preparation work |
| Incoming train | Push-off wait | Push-off submitted to push-off started (workers + passage track) |
| Incoming train | Push-off duration | Duration of the full push-off envelope (all sub-drives) |
| Incoming train | Total in arrival yard | Arrival on the arrival track to push-off completion |
| Incoming train | Total in system | Entry to push-off completion |
| Wagon group | Time in arrival yard | Arrival on arrival track to being pushed off |
| Wagon group | Time in classification yard | Arrival on classification track to departure of its outbound train |
| Wagon group | Idle time on classification track | Own securing/coupling completion to OBTP start |
| Outbound train | OBTP wait | OBTP submitted to OBTP started (workers + train locomotive) |
| Outbound train | OBTP duration | Duration of the outbound-train-preparation work |
| Outbound train | Exit-gate wait | Departure drive submitted to started (waiting for the exit gate) |
| Outbound train | Departure-drive duration | Departure-drive start to departure from the yard |
| Outbound train | Total on classification track | OBT formation to departure |

## 4.5 Model Inputs (Experimental Factors)

The model is driven by three data sets, and by a set of operating parameters that
govern activity durations and the yard's decision rules. Table 4.2 summarises them and
distinguishes fixed reference data from the *experimental factors* that may be varied
between simulation runs.

**Inbound traffic.** The arrival schedule specifies, for each inbound train, its
arrival time and its composition: the ordered sequence of wagon groups, and for each
wagon group its destination and its constituent wagons (from which its length is
derived). Because the model is trace-driven, this schedule is the primary determinant
of load and of temporal contention.

**Infrastructure.** The track layout specifies the set of arrival and classification
tracks, each with an identifier, an area assignment, and a length; and the geometric
distances used by the drive activities. All movement distances are pre-computed as
exact Euclidean polyline lengths from the surveyed node coordinates of the yard.

**Resource pool.** The staffing and traction configuration specifies the ground staff
(each with a yard-area assignment, a walking speed, and per-activity skill
multipliers), the number of shunting locomotives, the capacity of the road-locomotive
pool, and the capacity of the exit gate.

**Operating parameters.** These include the minimum accumulated length at which a
classification track is declared a complete outbound train, and the constants of the
activity-duration functions (drive speeds, the length penalty, the per-joint
preparation time, the fixed securing and coupling times, and the per-metre outbound
preparation rate).

**Table 4.2 — Model inputs.**

| Category | Item | Role |
|---|---|---|
| Inbound traffic | Train arrival times | Experimental factor (traffic intensity / timing) |
| Inbound traffic | Train composition (wagon groups, destinations, lengths) | Experimental factor |
| Infrastructure | Arrival / classification track set and lengths | Reference data (varied for capacity studies) |
| Infrastructure | Inter-point distances | Reference data (surveyed) |
| Resource pool | Ground-staff count, area split, walking speeds, skill multipliers | Experimental factor |
| Resource pool | Number of shunting locomotives | Experimental factor |
| Resource pool | Road-locomotive capacity | Experimental factor |
| Resource pool | Exit-gate capacity | Experimental factor |
| Operating parameter | Minimum train length for outbound formation | Experimental factor |
| Operating parameter | Drive speeds, length penalty, ITP / SEC / COP / OBTP constants | Calibration parameters |

## 4.6 Model Content: Scope

The model's *scope* defines its boundary — which components of the real system are
represented and which lie outside it.

**Spatial boundary.** The model represents the yard from the entry gate to the exit
gate. It comprises the arrival track group, the classification track group, and the
single connecting (passage) track between them, together with the decoupling point at
which cuts are detached during push-off. The wider rail network is represented only as
the source of arriving trains and the sink for departing trains.

**Temporal boundary.** For an individual train, the modelled horizon runs from its
entry into the yard to the departure of the outbound trains formed from its wagons. For
a run as a whole, the horizon spans the supplied arrival schedule and continues until
the last outbound train has departed.

**Included components.** Within this boundary the model represents: the arrival and
queueing of inbound trains; the assignment of arrival tracks; incoming-train
preparation; the sorting of wagon groups by destination; the push-off, including its
constituent multi-leg drives over the shared passage track; the accumulation, securing,
and coupling of wagon groups on classification tracks; the formation of outbound trains
once a length threshold is reached; outbound-train preparation; and departure through
the exit gate. It further represents the finite resources these activities require —
area-assigned ground staff, shunting locomotives, road locomotives, the passage track,
and the exit gate — and the travel time each mobile resource incurs in reaching its
work site.

**Excluded components.** The following are deliberately outside the model boundary, as
justified in Table 4.3: the wider network and its timetable beyond the yard gates;
hump- or gravity-based sorting (the yard is flat and loco-propelled); signalling,
interlocking, and track-circuit detail beyond the two modelled shared resources;
the dynamics of individual wagons and couplers; crew rostering, shift changes, and
breaks; equipment failure, maintenance, and weather; passenger operations; and
locomotive fuelling and servicing.

**Table 4.3 — Scope: components in and out of the model.**

| Component | In / Out | Justification |
|---|---|---|
| Inbound train arrival and entry queue | In | Source of load; entry-queue wait is a KPI |
| Arrival-track assignment and occupancy | In | A capacity constraint on the arrival side |
| Incoming-train preparation (ITP) | In | Major work phase; resource-consuming |
| Sorting and push-off (with sub-drives) | In | Core sorting mechanism of a flat yard |
| Passage track (single connecting track) | In | Principal physical bottleneck |
| Securing / coupling on classification tracks | In | Work phase governing classification dwell |
| Outbound-train formation and preparation | In | Reassembly phase; resource-consuming |
| Exit gate and departure drive | In | Departure-side bottleneck |
| Ground staff, shunting/road locomotives | In | Finite resources with travel time |
| Wider network / external timetable | Out | Beyond yard boundary; supplied as schedule |
| Hump / gravity sorting | Out | Not applicable to a flat yard |
| Signalling and interlocking detail | Out | Abstracted to the passage-track and exit-gate resources |
| Individual wagon / coupler dynamics | Out | Wagon groups are the atomic movement unit |
| Crew shifts, breaks, rostering | Out | Simplification; staff modelled as always available |
| Failures, maintenance, weather | Out | Simplification; out of study scope |

## 4.7 Model Content: Level of Detail

Whereas scope decides *which* components are present, the level of detail decides *how
finely* each is represented.

### 4.7.1 Entities

- **Inbound train** — an ordered set of wagon groups with an identifier, a total
  length, an arrival time, and an attached road locomotive. It exists from entry until
  it has been fully pushed off, after which its wagon groups continue independently.
- **Wagon group** — the atomic unit of movement. It carries a length, a destination,
  and a current location. It belongs to its inbound train until push-off, after which
  it becomes an independent entity standing on a classification track and, later, a
  constituent of an outbound train.
- **Cut (group of wagon groups)** — a transient entity representing a maximal run of
  *consecutive same-destination* wagon groups within an inbound train. A cut is the
  portion detached and rolled onto a classification track in a single push-off drive.
  Because the wagon groups of a cut are never uncoupled from one another, the cut is
  treated as a single unit on the classification side (see Section 4.8).
- **Outbound train** — the reassembled train formed on a classification track once the
  accumulated length threshold is met. It references its constituent wagon groups and
  carries a total length, a destination, and a road locomotive assigned for departure.
- **Wagon** — represented only to the extent that its length contributes to the length
  of its wagon group; individual wagons are not moved or handled.
- **Track** — an arrival or classification track with a length, an area assignment, an
  occupancy list, and a reservation flag.
- **Ground worker** — a resource entity with a yard-area assignment, a walking speed,
  and a set of per-activity skill multipliers.
- **Shunting locomotive** and **road locomotive** — traction resources (see Section
  4.7.2).

### 4.7.2 Resources and Capacities

The model represents five classes of finite resource:

- **Ground staff**, partitioned by yard area (arrival vs classification). A worker may
  only be allocated to an activity in its own area, reflecting the impracticality of
  staff walking between the widely separated track groups mid-task. Each allocation
  incurs a travel time to the work site and a return time to the rest area, computed
  from the worker's walking speed.
- **Shunting locomotives**, a finite pool used for incoming-train preparation and
  push-off. A shunting locomotive allocated for preparation is retained through the
  subsequent push-off, modelling the physical continuity of the same engine performing
  both.
- **Road (train) locomotives**, treated as effectively unlimited and allocated only for
  outbound-train preparation and departure; returned to the pool when a train departs.
- **The passage track**, a single shared resource of capacity one that connects the two
  track groups. It is required exclusively by the push-off and is held for the *entire*
  push-off envelope — including the pull-backs between successive sub-drives — because
  the locomotive continues to occupy the neck throughout.
- **The exit gate**, a single shared resource of capacity one required by the departure
  drive, which serialises outbound movements.

### 4.7.3 Activities and Durations

Each activity is characterised by the resources it requires and by a duration function.
In the current model all durations are deterministic. Table 4.4 summarises the
activities; the duration functions follow.

**Table 4.4 — Activities, resource requirements, and duration basis.**

| Activity | Resources | Duration basis |
|---|---|---|
| Arrival drive | Own road locomotive (no yard resource) | Distance/speed + length penalty |
| Incoming-train preparation (ITP) | 3 workers + 1 shunting locomotive | Separation joints + train-length walk |
| Push-off (envelope) | 3 workers + carried shunting locomotive + passage track | Sum of its sub-drives |
| Push-off drive (per cut) | Carried shunting locomotive | Two-leg distance/speed + cut-length penalty |
| Securing | 1 worker | Fixed |
| Coupling | 1 worker | Fixed |
| Outbound-train preparation (OBTP) | 2 workers + 1 road locomotive | Per-metre of train length |
| Departure drive | Exit gate (own road locomotive) | Distance/speed + length penalty |

The **drive activities** (arrival, push-off, departure) share a common form: a travel
term proportional to distance, plus a small length penalty representing the slower
acceleration and deceleration of a longer, heavier movement:

    t_drive = (d / v) × 60 + L × p

where d is the movement distance in metres, v the controlled speed in metres per
minute, L the length of the driven entity in metres, and p a length-penalty factor of
0.2 s/m. Arrival and departure drives use v = 333.33 m/min (20 km/h), representative of
restricted-speed yard running that accelerates from and decelerates to a stand. The
push-off drive uses v = 166.67 m/min (10 km/h), the cautious end of the 10–15 km/h band
typical of flat-yard shunting; its distance is the sum of two legs (arrival track to
decoupling point, and decoupling point to destination classification track), and its
length penalty applies to the length of the cut being pushed rather than the whole
train.

The **incoming-train-preparation** duration comprises two physically distinct terms:

    t_ITP = J × b × m  +  (L / w) × 60

The first (joint) term represents the hands-on separation work — uncoupling and fitting
or removing the removable link at each separation point that divides the train into
cuts. J is the number of separation joints (the number of consecutive same-destination
cuts minus one), b is a base time per joint of 90 s, and m is the average of the
allocated workers' preparation-skill multipliers. The second (walk) term represents the
workers walking the length L of the train (in metres) for visual checks and information
confirmation, at walking speed w (in metres per minute; the ground staff's station
movement speed, averaged over the allocated workers). A single-destination train has
zero separation joints, so its preparation reduces to the walking term alone — reflecting
that such a train retains its road locomotive until push-off and therefore requires no
separation work.

**Securing** and **coupling** are modelled as fixed-duration operations, independent of
how many wagon groups are in the arriving cut, since each is a single per-operation task:

    t_SEC = 120 × m       t_COP = 240 × m

where m is the relevant per-activity worker-skill multiplier. Securing (applying the
parking brake to hold a cut on an otherwise empty track) is the lighter operation;
coupling (connecting the incoming cut to the standing rake by screw coupling and air
hoses at a single interface) is heavier, in line with reported manual coupling times of
three to five minutes. **Outbound-train preparation** remains proportional to train
length: t_OBTP = L × 15 × m seconds, representing the brake test and departure
preparation of the assembled train.

### 4.7.4 Process Flow and Control Logic

The dynamic behaviour of the model is the lifecycle each entity follows and the rules
that govern it. Every activity emits three canonical events — *submitted* when it is
requested, *started* when its resources have arrived and work begins, and *completed*
when its duration has elapsed — giving a uniform three-timestamp record from which all
wait/duration KPIs are derived.

The end-to-end flow is as follows. An inbound train enters and joins the entry queue.
It is assigned a free arrival track (waiting, and re-checking at intervals of simulated
time, if none is available) and drives onto it. It then undergoes incoming-train
preparation, during which the sorting algorithm assigns each wagon group to a
destination classification track and the number of separation joints is determined.
The train is then pushed off: it is grouped into consecutive same-destination cuts,
and each cut is driven, in turn, over the passage track to its destination track. As a
cut arrives on a classification track it is secured (if the track was empty) or coupled
to the standing rake (if it was not). When a track's accumulated length reaches the
formation threshold, and once every cut on it has finished its securing or coupling, an
outbound train is declared. It undergoes outbound-train preparation and then, on
acquiring the exit gate, drives out of the yard.

Following Furian et al. (2015), this logic is organised into three hierarchical control
units:

- The **arrival control unit** governs the inbound side: the entry queue, arrival-track
  assignment, incoming-train preparation, the sorting of wagon groups to destination
  tracks, the computation of separation joints, and the push-off.
- The **classification control unit** governs the classification side: securing and
  coupling of arriving cuts, the train-completion check, the creation of outbound
  trains, outbound-train preparation, and the departure drive.
- The **resource control unit** is the central broker for all finite resources. Every
  activity's resource needs pass through it. It performs the area-filtered allocation of
  workers, the allocation of locomotives, the passage-track and exit-gate arbitration,
  and the travel-time scheduling that delays the start of work until resources have
  physically arrived.

Four operating rules complete the control logic. **Resource dispatch** follows a
first-in-first-out policy with bypass: the broker grants resources to the earliest
feasible request, allowing a later request to proceed ahead of an earlier one that is
currently infeasible, as a human yardmaster would rather than leave resources idle
behind a blocked job. **Within-track ordering** ensures that securing and coupling on a
single classification track commence in the order the cuts arrive, so that a cut cannot
couple to a rake before the preceding cut has been secured. **Destination
commitment** ensures that once a classification track has accumulated enough wagons to
begin forming an outbound train, subsequent wagon groups of the same destination are
routed to a fresh track rather than added to a track already committed to departure.
**Cut integrity** ensures that the wagon groups of a single cut, being already coupled
to one another, are secured or coupled as one unit rather than individually.

### 4.7.5 Events and State

The model's events are: the arrival of an inbound train; the submission, start, and
completion of each activity; the allocation, travel, arrival, and return of each mobile
resource; the claiming and release of the passage track and the exit gate; the crossing
of the train-length threshold on a classification track; the creation of an outbound
train; and its departure. The model's state, changed only at these events, comprises:
the contents of the entry queue; the occupancy and reservation status of every track;
the mapping from destinations to their current classification tracks; the set of tracks
already committed to an outbound train; the count of securing/coupling operations still
outstanding on each track; the ordering gate and deferred queue for each track; the
sets of available workers and locomotives; and the availability of the passage track
and the exit gate.

## 4.8 Assumptions

Assumptions are beliefs about the real system, or about aspects that are uncertain,
adopted where direct data is unavailable. Table 4.5 lists the principal ones with their
rationale.

**Table 4.5 — Assumptions.**

| Assumption | Rationale |
|---|---|
| A single connecting (passage) track joins the arrival and classification groups, of capacity one | The neck of a flat yard is a single physical track; two push-offs cannot proceed at once |
| The passage track is held for the entire push-off, including pull-backs between sub-drives | The locomotive continues to occupy the neck while repositioning the train |
| A single exit gate serialises departures | Reflects the yard's single departure route |
| Ground staff are localised to a yard area | Classification tracks are long (~800 m); staff do not walk between areas mid-task (confirmed by the operator) |
| Road locomotives are effectively unlimited | Departures are not, in practice, constrained by road-locomotive availability |
| Push-off runs at 10 km/h; arrival/departure at 20 km/h | Flat-yard shunting is controlled to 10–15 km/h; yard running is at restricted speed with acceleration from and deceleration to a stand |
| Manual coupling ≈ 4 min, securing ≈ 2 min | Reported manual screw-coupling times of 3–5 min; securing is the lighter operation |
| ITP effort = separation-joint work + a train-length walking check; a single-destination train needs only the walk | The road locomotive stays attached to a single-destination train until push-off, so no separation work is required |
| Movement distances are exact polyline lengths from surveyed node coordinates | Validated against stored segment lengths to within 0.5 m |
| Mobile resources travel a fixed representative distance to the work site | Detailed per-task geometry is not available; a representative distance is used |

## 4.9 Simplifications

Simplifications are deliberate reductions in model complexity, adopted to keep the model
tractable to build, run, and interpret. They are distinguished from assumptions in that
they are known departures from reality made for convenience rather than beliefs about an
uncertain system. Table 4.6 lists the principal ones.

**Table 4.6 — Simplifications.**

| Simplification | Justification |
|---|---|
| Durations are deterministic; arrivals are trace-driven from a fixed schedule | Establishes a reproducible baseline; stochastic variability is deferred to future work |
| An outbound train is formed on a single length threshold | The fuller length / wagon-count / weight criterion is reduced to length alone for this study |
| Wagon groups are the atomic movement unit | Individual wagons are represented only through their contribution to group length |
| Per-track movement distances are held in static maps | Distances change rarely; dynamic path computation is unnecessary at this level of detail |
| Congestion is modelled only through the passage track, the exit gate, and per-area staff contention | Detailed track-circuit and interlocking conflicts are outside the study's scope |
| Ground staff are always available (no shifts, breaks, or absence) | Staffing dynamics are outside the study's scope |
| No equipment failure, maintenance, or weather effects | Outside the study's scope; would add variability without addressing the objectives |
| Resource dispatch uses first-in-first-out with bypass | A tractable approximation of a human dispatcher matching free resources to feasible jobs |

The most consequential simplification is the deterministic, trace-driven treatment of
time. It yields a reproducible baseline in which the effect of a single changed input
can be isolated cleanly, which suits the diagnostic objectives of the present study.
It does, however, mean that the model reports the outcome of one particular realisation
rather than a distribution of outcomes; extending the activity durations and the arrival
process to sampled random variables, and reporting confidence intervals over
replications, is identified as the primary avenue for future development.

## 4.10 Summary

The conceptual model represents the Wiener Neustadt flat shunting yard as a
deterministic, trace-driven, resource-constrained discrete-event system, bounded by the
yard's entry and exit gates and organised, after Furian et al. (2015), into arrival,
classification, and resource control units. Its objectives are to quantify yard
performance and to diagnose the contributors to dwell time and the points of resource
contention; its outputs are a set of per-entity and aggregate KPIs that separate waiting
from working; its inputs are the inbound schedule, the infrastructure, the resource
pool, and a set of operating parameters. Its scope covers the full arrival-to-departure
cycle and its five classes of finite resource, while excluding the wider network,
hump sorting, fine signalling detail, and crew and failure dynamics. The assumptions
and simplifications above record where and why the representation departs from the real
system. This software-independent description is realised in the implementation
presented in the following chapter.

---

### References

Furian, N., O'Sullivan, M., Walker, C., Vössner, S., & Neubacher, D. (2015). A
conceptual modeling framework for discrete event simulation using hierarchical control
structures. *Simulation Modelling Practice and Theory*, 56, 82–96.

Robinson, S. (2008). Conceptual modelling for simulation Part I: definition and
requirements. *Journal of the Operational Research Society*, 59(3), 278–290.
