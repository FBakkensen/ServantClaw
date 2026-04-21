# Approval Checkpoint Contract

Use this structure at the end of the grill phase before asking for approval to implement.

## 1. Task

- Task ID and title
- Why it is the next unblocked task
- Dependency check

## 2. Requirements

- Distilled obligations from `tasks.md`
- Cited design, PRD, and user-story constraints that materially affect implementation

## 3. Important decisions

- List only the decisions that shape the implementation or test strategy
- Keep each decision short and explicit

## 4. Assumptions and open risks

- Include only the assumptions that still matter
- Flag unresolved risks that could change the approach

## 5. Test plan overview in Gherkin

Write a compact Gherkin-style overview using only the scenarios needed to prove the task done.

Example shape:

```gherkin
Feature: <task capability>

  Scenario: <happy path>
    Given ...
    When ...
    Then ...

  Scenario: <boundary or failure case>
    Given ...
    When ...
    Then ...
```

Prefer behavior-focused scenarios over implementation trivia.

## 6. Key flows

Show the core flow as one of:

- A Mermaid diagram when the flow is easier to understand visually
- A flat table when a concise textual flow is clearer

Keep the flow summary focused on the task's critical path, boundaries, and handoffs.

## 7. Implementation plan

- Summarize the intended code changes
- Map the plan to expected files, projects, or architectural layers when that can be inferred
- Keep it compact and execution-oriented

## 8. Approval request

End with a direct request for implementation approval.

Do not start coding until the user approves.
