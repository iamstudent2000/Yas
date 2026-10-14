# Workflow Engine — Spec v1.2 (owner decisions C1–C3 approved 2026-10-12)

Status: C1–C3 approved by the owner on 2026-10-12 (the recommendations below were accepted as written). Spec v1.1 wins on conflicts except where a row below is marked APPROVED.
Rows marked **GAP** are v1.1 requirements the code does not implement yet.

## 1. What the engine does today (as implemented)

- **Sequential path only.** A request has one approval path per round; step *n+1* starts after step *n* approves. One position per step.
- **Three ways to define a step** (`ApproverRuleKind`):
  1. `SpecificPosition` — a fixed position.
  2. `RequesterManagerLevel` — N levels above the requester's position.
  3. `EscalateToTreeLevel` — climbs from the requester's direct manager through **every** manager up to tree level N (top = level 1), one approval each, then continues to the next step. Steps are numbered "(i/n)".
- **Strict resolution** (`WorkflowStepResolver`): the configured path is followed exactly or submission is refused with every reason listed. No clamping to a lower manager, no skipping, no auto-bypass. A step may land on the requester's own position (self-approval) and the same position may appear twice.
- **Rounds.** Return to requester → resubmit opens a new round from step 1. Earlier rounds stay as history.
- **Append-only history:** `WorkflowStepDecision` (every decision, including reopened passes), `WorkflowRequesterAction` (submit/resubmit/cancel), `WorkflowFieldChange` (§11.3: field, old, new, user, position, time).
- **Closing.** Reject, requester cancel, SuperAdmin force-close (reason required) are terminal. Steps never reached are marked `Superseded`; recorded decisions are untouched.
- **Concurrency.** `Revision` token — two stale actors cannot both succeed.
- **Change protection (§18/§22.2).** Workflow steps cannot be edited while active requests of that type exist; the admin page shows the blockers and force-close is available.

## 2. Deviations from v1.1 that need a decision

| # | v1.1 text | Implementation | Proposal |
|---|-----------|----------------|----------|
| C1 | §23 forbids "Automatic manager escalation". | `EscalateToTreeLevel` walks managers up the tree. | **APPROVED — reword §23** to forbid *implicit* escalation (e.g. silently skipping/clamping). Escalation is allowed only when an admin explicitly configures it on a step. |
| C2 | §10.3 statuses: Draft, PendingApproval, InProgress, Approved, Rejected, Returned, Cancelled, Withdrawn. | PendingApproval, Approved, Rejected, ReturnedToRequester, Cancelled. Requester cancel and SuperAdmin force-close both end as `Cancelled` (distinguished by `ForceClosed*` fields / requester action). | **APPROVED:** drop Draft and InProgress (a request is submitted on creation; the current step is `CurrentStepOrder`). No `Withdrawn`: requester cancel and SuperAdmin force-close both stay `Cancelled`, told apart by `ForceClosed*` fields / requester action. Final status list: PendingApproval, Approved, Rejected, ReturnedToRequester, Cancelled. |
| C3 | §12.2/§12.4 per-step sequential or parallel, ANY/ALL. | One position per step, sequential. | **APPROVED:** sequential only (one position per step) for v1.2; parallel steps and ANY/ALL move to v1.3. **Update:** the owner has since asked for parallel steps next; built on `claude/parallel-steps` (see §3). |
| C4 | §10.2 resubmit "restarts from the beginning". | Same. | none |
| C5 | §22.1 reassign request to a different workflow. | Not implemented. | **DECLINED by owner (2026-10-12):** workflow reassignment is not wanted; force-close remains the only SuperAdmin resolution path. |

## 3. Gap list (v1.1 requirements not built)

| Priority | Item | Spec ref | Notes |
|----------|------|----------|-------|
| ~~High~~ Done | Notifications (in-app, unread/read, link) | §17 | Built on branch `claude/notifications`: submit/step reached/approved/rejected/returned/returned-to-step/force-closed, `/notifications` page with unread filter, sidebar badge. "Request reopened" and "delegation received" wait for Reopen and Delegation. |
| ~~High~~ Done | Reopen of a rejected request | §13.4 | Branch `claude/reopen`. Gated by the new permission `Requests.Reopen` (SuperAdmin has it automatically; it can be granted to other positions). Reason required; same round; earlier approvals kept; reopen recorded with actor and reason. Sequential steps only — the parallel-step rule waits for parallel steps. |
| ~~High~~ Done | Vacant approver → SuperAdmin notification | §5.4 | A step landing on a position with no active holder alerts every active SuperAdmin. Inactive holders count as vacant here. |
| ~~Medium~~ Done | ReturnToSpecificStep | §13.2 | Branch `claude/return-to-step`. Permission `Requests.ReturnToSpecificStep`. The approver picks any earlier step (offered when there are two or more); approvals after it are marked superseded (who approved stays visible) and must be given again; the decision records the target step. |
| ~~Medium~~ Done | Delegation + history ("acting for") | §15, §16 | Built on branch `claude/delegation`: `/delegations` page (create/revoke), per-step "allow delegation" switch, SuperAdmin replacement for any position, delegate sees items in inbox with "on behalf of" badge, decisions record who they acted for, delegate notified. The inbox history tab lists decisions a person made on behalf of another position, labelled "on behalf of", and labels decisions on your position that a delegate made. |
| ~~Medium~~ Built, awaiting build/test run | Parallel steps, ANY/ALL, reject policy | §12.2–§12.4, §14 | Branch `claude/parallel-steps`. Sibling step definitions/rows share one `Order` (no CSV/JSON, no extra table). A group has one approval mode (**ALL** = everyone approves, **ANY** = first approval completes it; the rest become Superseded) and one reject policy (**Immediate** = first rejection closes the request, **WaitForAll** = closes only after every position answered and the group is unsatisfied; with ANY a later approval still completes it). Mode/policy are copied onto each request step at submission. Rules: escalation steps cannot be in a group, members must agree on mode/policy, one position cannot appear twice in a group (resolver refuses with a reason). Return-to-previous/-specific makes the whole group decide again and resets siblings' approvals. Reopen after a rejection asks only the rejecting position(s) again; approvals already given stay. Notifications: one per waiting position; a partial approval and a not-yet-final wait-for-all rejection notify nobody. Admin editor (branch `claude/workflow-stages`): the path is shown as stage cards — one card per order, approvers inside it, mode and reject policy set once in the card header, "add parallel approver" / "add stage after" / split / move-stage actions, and an "آزمایش مسیر" panel that resolves the path for a chosen requester position with the real resolver (holders, vacancies, inactive positions, refusal reasons). Schema change: the unique `(WorkflowType, Order)` and `(RequestId, Round, Order)` indexes are relaxed; new columns `ApprovalMode`, `RejectPolicy` — dev DB recreated. |
| Dropped | Workflow editor: drag-and-drop of stages/approvers, copy a path from another workflow type | — | Owner does not want these (2026-10-14). The stage-card editor and the path preview stay as built; no further editor work planned. |
| Dropped | Workflow reassignment | §22.1 | Owner does not want this feature (2026-10-12). Not built; the branch `claude/reassign-workflow` must not be merged. |
| ~~Low~~ Already satisfied | Inactive employees treated as vacant | §4.3 | Checked: deactivating an employee (Users page) ends all their active position assignments, so their positions become vacant at once and no inactive holder can receive a step. Notifications also skip inactive holders as a second guard. No code change needed. |
| ~~Low~~ Done (requests) | Request list filters (status, type, requester, date, current step) and search by request ID | §25 | Branch `claude/list-filters`: one filter bar on My requests, Inbox (pending), Active requests (now paginated too) and Rejected requests; the short ID `#xxxxxxxx` is shown on every request. Employees, positions, audit log and notification lists: see the next row. |
| ~~Low~~ Done (partly) | Other list filters | §25.2 | Branch `claude/list-filters-2`: Employees (status, organization, current position + existing search), Audit log (entity ID added to the existing user/position/entity/action/date filters), Positions (occupied/vacant + existing name/code search), Notifications (read/unread existed). Position department/active filters: done with the position lifecycle row below. |
| ~~Medium~~ Done | Position deactivation, department link, change protection | §3.1–§3.4, §19, §22.2, §26.3 | Branch `claude/position-lifecycle`: positions get an active flag and an optional department (organization); the tree has active/inactive and department filters. Deactivating is refused while the position has an occupant, is a fixed approver in a workflow, or any active request waits on it or was submitted from it — the blocking requests are listed with a link to force-close. Re-parenting moves a whole branch and is refused for the same reason. A deactivated position cannot be assigned, chosen in workflow steps or delegations, and a request whose path crosses one is refused. Deletion is refused for any position with history (assignments, permissions, workflow steps, requests, approvals, delegations). A child must be in the same department as its parent when both have one; making department mandatory for non-root positions is not enforced (existing data). Schema change: dev DB recreated. |
| ~~Low~~ Dropped | Requester-editable Draft status | §10.2 | Removed by decision C2 (no Draft status in v1.2). |

## 4. Suggested order

1. ~~Owner decides C1–C3~~ — done (see §2).
2. Notifications + vacancy alert (they unblock most of the other flows).
3. Reopen, then ReturnToSpecificStep.
4. Delegation.
5. Parallel steps — deferred to v1.3 (decision C3).
