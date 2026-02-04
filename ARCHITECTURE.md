# ModernPOS Architecture (Offline-first + PostgREST Sync)

## Goal
A Point-of-Sale system where each terminal can operate fully offline using a local SQLite database.
A Sync Agent handles background synchronization to a central Postgres database exposed via PostgREST.

## Components

### 1) Terminal App (Avalonia) - `Pos.Terminal`
**Responsibilities**
- Runs the POS UI (product browsing, cart, checkout, customers, inventory views).
- Reads/writes local data ONLY from SQLite (no direct dependency on remote Postgres).
- Works offline 100% of the time.
- Writes business events (sales, customer updates, inventory adjustments) into an Outbox table for syncing.

**Local storage**
- SQLite database file stored per machine (e.g. `%ProgramData%\ModernPOS\terminal.db`).

**Networking**
- Terminal should not depend on remote availability.
- Terminal may show sync status (last sync, pending uploads, errors) but does not perform sync itself.

---

### 2) Local Data Layer - `Pos.Local` (new project)
**Responsibilities**
- Owns the SQLite schema and migrations.
- Provides repositories/services used by `Pos.Terminal`.
- Implements:
  - Local entities (Products, Customers, Sales, Inventory, etc.)
  - Outbox queue for sync
  - Sync state (last pulled timestamps, etc.)

---

### 3) Sync Agent - `Pos.SyncAgent`
**Responsibilities**
- Runs in background (console for dev; Windows Service later).
- Push local changes to server via PostgREST:
  - Reads unsent Outbox rows from SQLite
  - POST/PATCH/DELETE to PostgREST
  - Marks Outbox rows as sent (or increments attempts + stores error)
- Pull server updates down to terminal via PostgREST:
  - GET changes since last pull per table
  - Upsert into SQLite
  - Update sync_state checkpoints

**Sync strategy (V1)**
- IDs are UUIDs everywhere to avoid collisions offline.
- Conflict policy (V1): last-write-wins using `updated_at` timestamps.
- Soft deletes (optional): `deleted_at` timestamps rather than hard delete.

---

### 4) Server (Central) - Postgres + PostgREST
**Responsibilities**
- Source-of-truth database for all stores/terminals.
- Exposes REST endpoints via PostgREST.
- Uses roles/RLS policies for security (later phase).

**Server schemas**
- Tables mirror terminal entities with added fields:
  - `store_id`, `terminal_id`
  - `updated_at` (server authoritative)
  - `deleted_at` (optional)

---

## Data Model (Minimum Viable Entities)

### Shared entities (exist both locally + server)
- products
- customers
- sales
- sale_lines
- inventory (per location/store)

### Local-only entities
- outbox (queued operations to upload)
- sync_state (checkpoints per table)
- device_config (store_id, terminal_id, api base url, auth token, etc.)

---

## End-to-End Flow (Checkout)
1. Terminal loads products from SQLite.
2. Cashier adds items → cart.
3. Checkout creates:
   - sale + sale_lines in SQLite
   - outbox event(s) representing the sale
4. SyncAgent uploads outbox events to PostgREST.
5. SyncAgent pulls any server changes and applies locally.

---

## Non-Goals for V1 (later)
- Multi-cashier shifts / permissions
- Price rules/promos
- Hardware integration (printers, barcode scanners)
- Advanced conflict resolution
