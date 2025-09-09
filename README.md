# MicroHttpChallange
A teaching project with regards to microservices and the http protocol

## Design 

## Overview

This project implements a small microservices ecosystem intended for two purposes:

1. **Educational** — demonstrate microservice design patterns (auth, registration, inter-service auth).
2. **Competitive** — run a competition where participants receive intentionally _broken HTTP packages_ and must repair them. Progress is tracked per participant; a scoreboard ranks competitors.

The system is composed of small, single-responsibility services with clear APIs and stateless front-ends where appropriate. The design emphasizes security, observability, and the ability to scale horizontally.

---
## Goals & Requirements

### Functional

- Competitors can register and authenticate.
- Competitors request broken HTTP packages from the Competition Service (REST GET) and submit fixes.
- The Competition Service keeps a per-user progress record (how many problems solved, which ones solved).
- Scoreboard API exposes ranking and progress. First to solve all problems wins; otherwise highest solved count wins.
- Admin endpoints to upload / manage broken packages, view logs, and reset competitions.

### Non-functional

- Secure (TLS in transit, hashed passwords, strong auth between services).
- Scalable to many concurrent competitors.
- Observable (metrics, traces, logs).
- Resilient (graceful degradation, retries, rate limits to avoid abuse).

---
## High-level Architecture

- **Auth Service**: authenticates users and issues short-lived tokens for clients and service-to-service tokens for microservices.
- **Registration Service**: handles user creation, password reset flow, email verification (optional).
- **Competition Service**: stores problems (broken HTTP packages), serves problems, receives submissions, validates fixes, updates progress.
- **Scoreboard Service**: aggregates progress to build leaderboards and exposes public scoreboard API.
- **Data Stores**: Azure tables to keep storage cheap

---
## Services Detailed

### 1) Registration Service

**Responsibility**: Create and manage competitor accounts.

**Key APIs**:

- `POST /register` — payload: `{ username, password, email? }` -> returns 201 or error.
- `POST /login` — forward to Auth Service or perform delegated auth flow.
- `POST /password-reset` — optional.

**Implementation notes**:

- Validate username (unique) and strong password policy.
- Store password hashed with bcrypt/argon2.
- On success, optionally request token from Auth Service and return to client.
### 2) Auth Service

**Responsibility**: Authenticate users, issue tokens (JWT), manage service-to-service auth.

**Features**:

- JWT access tokens (short lifespan, e.g., 5-15 min) and refresh tokens (longer, stored server-side).
- Support for mutual TLS or signed tokens for inter-service requests.
- Token introspection endpoint for services that want to validate tokens.

**APIs**:

- `POST /token` — grants token given credentials or refresh token.
- `POST /introspect` — validates token (used by gateway/services).

**Security**:

- Rotate signing keys periodically.

### 3) Competition Service

**Responsibility**: Serve broken HTTP packages, accept submissions, validate fixes, track progress.

**Data model** (core tables):

- `problems` — id, title, description, problem_payload (raw broken HTTP package), order_number
- `users_progress` — user_id, problem_id, status (unseen/in_progress/solved/failed_attempts), attempts_count, first_solved_at
- `submissions` — id, user_id, problem_id, submitted_payload, result (accepted/rejected), feedback, created_at

**APIs**:

- `GET /problem` — returns the next unsolved problem for the authenticated user: `{broken_package}`
- `POST /problems/{id}/submit` — http package -> returns acceptance status and feedback.
- Admin: `POST /problems` to add problems, `PUT /problems/{id}` to update.
- Admin: `GET /problems` list current problems with `id, title, description, order_number`

**Validation**:

- Define deterministic validation logic:  Run a small parser to assert correctness.

**Session / Rate-limiting**:

- Limit `GET /problem` per user to avoid spamming.

**Progress tracking**:

- Each time a submission is accepted, mark `users_progress` entry and increment solved count.
- Emit an event/message to Scoreboard Service (via eventgrid or messagebus).

### 4) Scoreboard Service

**Responsibility**: Maintain current rankings and respond to scoreboard queries.

**Approach**:

- Receive events about problem solves. Persist authoritative data in Azure data table for auditing.

**APIs**:

- `GET /scoreboard` — returns top N competitors with solved_count, solved_at.
- `GET /scoreboard/{username}` — returns single user's status.

**Ranking rules**:

- Primary key: number of solved problems (descending).
- Tie-breaker: earliest time to reach that solved count (earlier wins).
- Special rule: first to reach `total_number_of_problems` is immediately declared winner .

---
## Inter-service Communication

- Prefer **HTTP + mTLS** or HTTP + JWT verified by Auth Service (introspection) for simplicity.
- Use asynchronous messaging (eventgrid or messagebus) for high-throughput events (submission accepted) to decouple Competition from Scoreboard.

---
## Data Stores & Schemas

- **Azure datatable**: users, problems, submissions, progress (ACID data).

Sample `problems` table columns: `id UUID PK`, `title TEXT`, `description TEXT`, `broken_payload BLOB`, `created_at TIMESTAMP`.

---
## Security Considerations

- Always use TLS between client and gateway and service-to-service.
- Sanitize inputs — problems and submissions may contain weird raw HTTP bytes; avoid executing these directly.
- Store JWT secrets/keys in Vault and rotate keys.

---
## Anti-cheat / Fairness

- Prevent re-sharing of solutions: add random data to each users problem and validate when the users posts their solution. 

---
## API Examples (JSON)

**Get next problem**  
`GET /api/v1/problem` (auth: Bearer)  
Response: `<broken package>`

**Submit fix**  
`POST /api/v1/problems/{id}/submit`  
Body: `<raw HTTP message>`  
Response: `{ "accepted": true, "feedback": "OK" }`

---
## Operational Concerns

### Monitoring

- Some monitor solutions, such as application insights or similar to discover errors

---
## Testing & Validation

- Unit tests for validation logic (HTTP packet normalization & comparison).
- Integration tests: registration -> auth -> request problem -> submit -> scoreboard update.
 
---
## Deployment / Dev Workflow

- Local: Docker Compose with Azurite
- Prod: Azure services.
- CI: run tests, build images, push to registry, deploy to staging, run smoke tests, promote to prod.

---
## Open Design Questions (to decide)

- Exact format/spec of the "broken HTTP package" and what counts as a correct repair (binary identical, normalized grammar, or idempotent semantics?).
- Offline vs realtime scoreboard (real-time requires Redis + events).

---
## Appendix

- Sample normalization approach: parse HTTP with a strict parser, canonicalize header order (alphabetical), canonicalize line endings to `\r\n`, remove duplicate headers by rules, then compare canonicalized strings or compute canonical hash.
- Example validation: allow multiple correct solutions by storing a set of canonical hashes.
