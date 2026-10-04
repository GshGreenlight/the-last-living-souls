# Last-Living-Souls

## Unity Version

6000.3.21f1

## Tips for Developers

- NEVER work on the same scene in different branches. Unity scenes are difficult to merge reliably.
- If something can be implemented directly in the Unity Editor, prefer doing it there.

## Branching Workflow

- For each feature, create a branch named `feature-*name*`.
- When the feature is ready, open a merge request into `dev` and assign your lead as a reviewer.
- You cannot push directly to `main`. All changes must go through a merge request.
- Before a merge request can be merged, it must be reviewed and approved by at least one other team member.
- CI will be implemented later.
