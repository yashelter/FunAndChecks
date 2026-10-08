# Preserved interface

Snapshot of the previous frontend, taken before the dashboard redesign. Pages,
dialogs, layouts and UI helpers are independent of the new UI. Only navigation
was prefixed with `/legacy`; API clients, authentication, DTOs and translations
continue to come from `Frontend.Shared`.

The application loads `legacy.css` instead of the dashboard stylesheet when it
starts on `/legacy`. Switching UI versions performs a full page navigation to
isolate their styles and theme providers. Existing API and SignalR paths stay at
the origin root. Both interfaces see the same live data and authentication tokens.

Do not automatically apply new UI changes here. Changes to this snapshot should
be limited to necessary compatibility/security fixes and the version switch.
