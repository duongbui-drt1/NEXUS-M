# NEXUS M — Repository Bootstrap

## Repository
- Name: `NEXUS-M`
- Default branch: `main`
- Initial documentation branch: `docs/m-product-engineering-baseline`
- Commit message: `docs(product): add NEXUS M product and engineering baseline`

## Recommended controls
Protect `main`; require pull requests and passing CI; disable force pushes and protected-branch deletion; enable secret scanning and dependency/security alerts; use least-privilege access. Do not merge, release or deploy without explicit approval.

## Initial structure
```
.
├── README.md
├── docs/
│   ├── product/
│   │   ├── 01-PRODUCT-STRATEGY.md
│   │   └── 02-PRD-1.0.md
│   ├── architecture/
│   │   ├── 03-SYSTEM-ARCHITECTURE.md
│   │   └── 04-DOMAIN-MODEL.md
│   ├── engineering/
│   │   ├── 00-MASTER-ENGINEERING-PROMPT.md
│   │   ├── 05-ROADMAP-AND-ACCEPTANCE.md
│   │   └── 06-REPOSITORY-BOOTSTRAP.md
│   └── decisions/
└── .github/workflows/
```

## First execution prompt
Read README and all docs. Execute Phase 0 only from the Master Engineering Prompt. Inspect repository and accessible NEXUS C contracts. Do not scaffold the app, add dependencies, create migrations or implement features. Return evidence-backed findings, risks, integration map and prioritized Phase 1 backlog. Stop for review.

## Decisions to verify
Actual C auth/session and authorization contracts; tenant/workspace/org semantics; supported .NET/EF Core/PostgreSQL/frontend versions; API versioning and event envelope; CI/deployment/secrets conventions; retention, audit and backup/restore.

## Safety
Never delete or overwrite existing work without inspection. Never reset branches, force-push, expose secrets, weaken access controls, delete DB volumes, merge, release or deploy without explicit approval.
