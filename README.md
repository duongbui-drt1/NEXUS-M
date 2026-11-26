# NEXUS M

**Universal Enterprise Management & Decision Intelligence Platform**

NEXUS M is the management and decision-intelligence layer of the NEXUS ecosystem. It serves organizations of different sizes through progressive module activation and configurable organizational structures.

## Product boundaries
- **NEXUS C** owns identity, users, tenants, memberships, authentication, authorization and platform entitlements.
- **NEXUS M** owns management workflows, KPI definitions, management read models, executive intelligence, governance and management reporting.
- **NEXUS F** owns authoritative financial transactions and accounting.
- **NEXUS E** owns authoritative logistics and transport execution.

M integrates through versioned APIs and events. It must not access another application's private database or duplicate authoritative records.

## Documentation
- [Master Engineering Prompt](docs/engineering/00-MASTER-ENGINEERING-PROMPT.md)
- [Product Strategy](docs/product/01-PRODUCT-STRATEGY.md)
- [PRD 1.0](docs/product/02-PRD-1.0.md)
- [System Architecture](docs/architecture/03-SYSTEM-ARCHITECTURE.md)
- [Domain Model](docs/architecture/04-DOMAIN-MODEL.md)
- [Roadmap & Acceptance](docs/engineering/05-ROADMAP-AND-ACCEPTANCE.md)
- [Repository Bootstrap](docs/engineering/06-REPOSITORY-BOOTSTRAP.md)

## Status
Documentation baseline v1.0; proposed, not implemented or production-verified. Start with Phase 0 audit only. Do not generate the application before architecture and integration gates are approved.
