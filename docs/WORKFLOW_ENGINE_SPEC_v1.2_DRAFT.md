# Workflow Engine — Spec v1.2 (DRAFT, needs owner approval)

Status: proposal. Spec v1.1 wins on conflicts (its Purpose section), so every row marked
**CHANGE** below is a deviation from v1.1 that the owner must approve before it becomes rule.
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
| C1 | §23 forbids "Automatic manager escalation". | `EscalateToTreeLevel` walks managers up the tree. | **CHANGE:** reword §23 to forbid *implicit* escalation (e.g. silently skipping/clamping). Escalation is allowed only when an admin explicitly configures it on a step. |
| C2 | §10.3 statuses: Draft, PendingApproval, InProgress, Approved, Rejected, Returned, Cancelled, Withdrawn. | PendingApproval, Approved, Rejected, ReturnedToRequester, Cancelled. Requester cancel and SuperAdmin force-close both end as `Cancelled` (distinguished by `ForceClosed*` fields / requester action). | **CHANGE:** drop Draft and InProgress (a request is submitted on creation; the current step is `CurrentStepOrder`); add `Withdrawn` for requester cancel, or document the single-status rule. |
| C3 | §12.2/§12.4 per-step sequential or parallel, ANY/ALL. | One position per step, sequential. | **CHANGE or GAP:** either accept "sequential only" for v1.2, or schedule parallel steps (see §3). |
| C4 | §10.2 resubmit "restarts from the beginning". | Same. | none |
| C5 | §22.1 reassign request to a different workflow. | Not implemented. | **GAP** |

## 3. Gap list (v1.1 requirements not built)

| Priority | Item | Spec ref | Notes |
|----------|------|----------|-------|
| ~~High~~ Done | Notifications (in-app, unread/read, link) | §17 | Built on branch `claude/notifications`: submit/step reached/approved/rejected/returned/returned-to-step/force-closed, `/notifications` page with unread filter, sidebar badge. "Request reopened" and "delegation received" wait for Reopen and Delegation. |
| ~~High~~ Done | Reopen of a rejected request | §13.4 | Branch `claude/reopen`. Gated by the new permission `Requests.Reopen` (SuperAdmin has it automatically; it can be granted to other positions). Reason required; same round; earlier approvals kept; reopen recorded with actor and reason. Sequential steps only — the parallel-step rule waits for parallel steps. |
| ~~High~~ Done | Vacant approver → SuperAdmin notification | §5.4 | A step landing on a position with no active holder alerts every active SuperAdmin. Inactive holders count as vacant here. |
| ~~Medium~~ Done | ReturnToSpecificStep | §13.2 | Branch `claude/return-to-step`. Permission `Requests.ReturnToSpecificStep`. The approver picks any earlier step (offered when there are two or more); approvals after it are marked superseded (who approved stays visible) and must be given again; the decision records the target step. |
| ~~Medium~~ Done | Delegation + history ("acting for") | §15, §16 | Built on branch `claude/delegation`: `/delegations` page (create/revoke), per-step "allow delegation" switch, SuperAdmin replacement for any position, delegate sees items in inbox with "on behalf of" badge, decisions record who they acted for, delegate notified. Limitation: a delegate's own history tab does not list decisions made on behalf of others. |
| Medium | Parallel steps, ANY/ALL, reject policy | §12.3, §14 | Needs a step-targets table (no CSV/JSON). Biggest change; touches resolver, `WorkflowRequestStep`, inbox queries. |
| Medium | Workflow reassignment | §22.1 | Needs a superseded-history marker on steps. |
| ~~Low~~ Already satisfied | Inactive employees treated as vacant | §4.3 | Checked: deactivating an employee (Users page) ends all their active position assignments, so their positions become vacant at once and no inactive holder can receive a step. Notifications also skip inactive holders as a second guard. No code change needed. |
| ~~Low~~ Done (requests) | Request list filters (status, type, requester, date, current step) and search by request ID | §25 | Branch `claude/list-filters`: one filter bar on My requests, Inbox (pending), Active requests (now paginated too) and Rejected requests; the short ID `#xxxxxxxx` is shown on every request. Employees, positions, audit log and notification lists are not covered by this change. |
| Low | Requester-editable Draft status | §10.2 | Depends on C2. |

## 4. Suggested order

1. Owner decides C1–C3.
2. Notifications + vacancy alert (they unblock most of the other flows).
3. Reopen, then ReturnToSpecificStep.
4. Delegation.
5. Parallel steps (only if C3 says so).
