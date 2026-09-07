# Error-State Gallery Scenarios Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add one catalog-backed, equivalently rendered error-state scenario to the React and Blazor gallery lanes for text box, text area, input, select field, and number field.

**Architecture:** Each module's authored `scenarios.json` gains one `invalid-input` scenario tied only to existing conformance cases. Its existing React and Blazor scenario dispatcher gains one branch that renders the real component invalid prop with the same value and accessible name, and each Storybook catalog gains the matching named story. Generated gallery catalogs and evidence remain generator-owned.

**Tech Stack:** JSON scenario catalogs, React/TypeScript Storybook, Blazor/Razor Storybook, Node.js UI gates.

**Spec:** User-provided Ticket 098 `assertEmptyAndErrorStates` requirements in this session.

## Global Constraints

- Add only the named error state for each requested module; add no empty states.
- Use scenario id `<mod>.error-state`, name `Error state`, and surface `invalid-input`.
- Reuse one or two existing conformance fixture ids and leave `sourceQualityCaseIds` empty.
- Render real component props and equivalent content in both lanes, including accessible names.
- Do not touch any file under `tooling/`, do not run `git commit`, and do not ask for approval.
- Preserve all pre-existing worktree changes.

---

### Task 1: Confirm the missing error-state gate behavior

**Files:**
- Read: `docs/evidence/gate-model/ui-gate-model.json`

**Interfaces:**
- Consumes: Existing module scenario declarations and gallery stories.
- Produces: A red baseline where all five `assertEmptyAndErrorStates` gates are `PARTIAL`.

- [x] **Step 1: Run the gate model before implementation**

Run: `node tooling/gates/run-ui-gate-model.mjs`

- [x] **Step 2: Confirm each requested module is red**

Expected: `hlp.ui.text-box`, `hlp.ui.text-area`, `hlp.ui.input`, `hlp.ui.select-field`, and `hlp.ui.number-field` each report `PARTIAL`.

### Task 2: Add text-box error state

**Files:**
- Modify: `specs/modules/ui/hlp.ui.text-box/scenarios.json`
- Modify: `gallery/projections/react/src/TextBox.stories.tsx`
- Modify: `gallery/projections/blazor/Stories/TextBoxScenario.razor`
- Modify: `gallery/projections/blazor/Stories/TextBox.stories.razor`

**Interfaces:**
- Consumes: React `TextBox error` and Blazor `ShipyardTextBox Error`, with `Vessel name` as the accessible name.
- Produces: `text-box.error-state` rendered in both gallery lanes.

- [ ] **Step 1: Append the authored invalid-input scenario using `text-box.validation`**
- [ ] **Step 2: Add equivalent React and Blazor branches rendering `Invalid berth assignment`**
- [ ] **Step 3: Add the `Error state` story in both lanes**
- [ ] **Step 4: Run generation, drift, TypeScript, gate-model, and per-module status verification**

### Task 3: Add text-area error state

**Files:**
- Modify: `specs/modules/ui/hlp.ui.text-area/scenarios.json`
- Modify: `gallery/projections/react/src/TextArea.stories.tsx`
- Modify: `gallery/projections/blazor/Stories/TextAreaScenario.razor`
- Modify: `gallery/projections/blazor/Stories/TextArea.stories.razor`

**Interfaces:**
- Consumes: React `TextArea error` and Blazor `ShipyardTextArea Error`, both with `Inspection notes` as the accessible name.
- Produces: `text-area.error-state` rendered in both gallery lanes.

- [ ] **Step 1: Append the authored invalid-input scenario using `text-area.error-alias` and `text-area.form-context`**
- [ ] **Step 2: Add equivalent React and Blazor branches rendering `Invalid inspection notes`**
- [ ] **Step 3: Add the `Error state` story in both lanes**
- [ ] **Step 4: Run generation, drift, TypeScript, gate-model, and per-module status verification**

### Task 4: Add input error state

**Files:**
- Modify: `specs/modules/ui/hlp.ui.input/scenarios.json`
- Modify: `gallery/projections/react/src/Input.stories.tsx`
- Modify: `gallery/projections/blazor/Stories/InputScenario.razor`
- Modify: `gallery/projections/blazor/Stories/Input.stories.razor`

**Interfaces:**
- Consumes: React `Input invalid` and Blazor `ShipyardInput Invalid`, both with `Structure identifier` as the accessible name.
- Produces: `input.error-state` rendered in both gallery lanes.

- [ ] **Step 1: Append the authored invalid-input scenario using `input.invalid` and `input.invalid-appearance`**
- [ ] **Step 2: Add equivalent React and Blazor branches rendering `Unrecognized structure`**
- [ ] **Step 3: Add the `Error state` story in both lanes**
- [ ] **Step 4: Run generation, drift, TypeScript, gate-model, and per-module status verification**

### Task 5: Add select-field error state

**Files:**
- Modify: `specs/modules/ui/hlp.ui.select-field/scenarios.json`
- Modify: `gallery/projections/react/src/SelectField.stories.tsx`
- Modify: `gallery/projections/blazor/Stories/SelectFieldScenario.razor`
- Modify: `gallery/projections/blazor/Stories/SelectField.stories.razor`

**Interfaces:**
- Consumes: React `SelectField error` and Blazor `ShipyardSelectField Error`, the same `Inspection status` name, and the same three options.
- Produces: `select-field.error-state` rendered in both gallery lanes.

- [ ] **Step 1: Append the authored invalid-input scenario using `select-field.invalid-required` and `select-field.form-field`**
- [ ] **Step 2: Add equivalent React and Blazor branches with selected value `pending`**
- [ ] **Step 3: Add the `Error state` story in both lanes**
- [ ] **Step 4: Run generation, drift, TypeScript, gate-model, and per-module status verification**

### Task 6: Add number-field error state

**Files:**
- Modify: `specs/modules/ui/hlp.ui.number-field/scenarios.json`
- Modify: `gallery/projections/react/src/NumberField.stories.tsx`
- Modify: `gallery/projections/blazor/Stories/NumberFieldScenario.razor`
- Modify: `gallery/projections/blazor/Stories/NumberField.stories.razor`

**Interfaces:**
- Consumes: React `NumberField error` and Blazor `ShipyardNumberField Error`, both with `Inspection quantity` as the accessible name.
- Produces: `number-field.error-state` rendered in both gallery lanes.

- [ ] **Step 1: Append the authored invalid-input scenario using `number-field.invalid` and `number-field.description`**
- [ ] **Step 2: Add equivalent React and Blazor branches rendering value `128`**
- [ ] **Step 3: Add the `Error state` story in both lanes**
- [ ] **Step 4: Run generation, drift, TypeScript, gate-model, and per-module status verification**

### Task 7: Final verification and evidence review

**Files:**
- Verify: All twenty authored scenario/story files above.
- Verify generated outputs: `gallery/scenarios/hlp.ui.*.json`, `gallery/tests/tests/gallery.spec.ts`, `docs/provenance/source-map.yaml`, and `docs/evidence/gate-model/ui-gate-model.json`.

**Interfaces:**
- Consumes: Five individually green error-state implementations.
- Produces: Exact final drift output and the count of requested modules moved from `PARTIAL` to `PASS`.

- [ ] **Step 1: Run the complete requested verification commands fresh**
- [ ] **Step 2: Confirm all five `assertEmptyAndErrorStates` statuses are `PASS`**
- [ ] **Step 3: Inspect the final diff for scope, accessibility, equivalent strings, and absence of `tooling/` edits caused by this work**
- [ ] **Step 4: Report per-module PASS/FAILED, exact drift output, and count moved to PASS**
