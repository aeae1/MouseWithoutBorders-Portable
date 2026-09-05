# AI-assisted development identity

This portable fork is being modified interactively for aeae1 with assistance from ChatGPT.

Preferred attribution for assistant-driven commits:

`AI-Assisted-By: aeae1's vibe coding assistant — ChatGPT GPT-5.6 Sol`

The GitHub connector authenticates as aeae1, so GitHub will record the authenticated account as the commit author/committer for connector-created commits. The attribution trailer above is used to distinguish changes made with the assistant from hand-authored changes without requiring a separate bot account or credential.

## Working rules

- Keep `main` product-only and review `microsoft/PowerToys` updates for selective ports.
- Do custom work on `main` or short-lived descendant feature branches.
- Prefer small, reviewable commits.
- Preserve Mouse Without Borders protocol behavior unless an intentional compatibility break is documented.
- Preserve clipboard and file-transfer functionality.
- Add or update focused tests for behavior changes where practical.
- Keep a Windows CI build focused on the Mouse Without Borders projects.
