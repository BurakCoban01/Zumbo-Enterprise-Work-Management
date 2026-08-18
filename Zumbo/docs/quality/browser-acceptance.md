# Browser and Accessibility Acceptance

Visible product changes require rendered evidence against a real backend when API state, authorization or realtime behavior matters.

## Core surfaces

Review the affected desktop and mobile workflows, including:

- home, personal work, inbox and notifications;
- projects, board, list, backlog and sprint;
- work-item detail and collaboration;
- planning, reports, intake and automation;
- portfolios, goals, capacity and knowledge;
- teams, audit and settings.

## States

Verify loading, empty, populated, validation error, server error, denied, offline and conflict states where applicable. For optimistic operations, confirm rollback and realtime reconciliation.

## Viewports and themes

Use breakpoints that exercise desktop, constrained desktop/tablet and mobile adaptation. Shared visual changes require light and dark theme review. Touch targets, internal scrolling and text containment must remain usable without incoherent overlap.

## Accessibility

Check keyboard navigation, visible focus, semantic names, heading structure, dialog focus management, contrast, zoom, reduced motion and forced-color resilience. Automated checks supplement rather than replace representative keyboard and assistive-technology review.

## Evidence handling

Browser screenshots, traces and console logs are generated test output. Keep only a small representative product gallery in `docs/media`; store run-specific evidence as CI artifacts.
