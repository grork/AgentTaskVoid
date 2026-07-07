# Interaction / Response Round-Trip

Questions about the return path from the taskbar back into the consumer: what
receives Shell activations (buttons, text input, deep links), and how a user's
answer gets delivered to the script/agent that asked.

Operator direction (2026-07-02, discovery): display-first -- raise and explore
these questions, but v1 may ship display-only; these are candidates for
deferral in answer sessions if the round-trip balloons.

## Questions

- [`INTER-1`: What receives Shell activations from buttons / text input](./INTER-1-what-receives-shell-activations-from-buttons-text-input.md) -- DEFERRED
- [`INTER-2`: Routing a received response back to the waiting consumer](./INTER-2-routing-a-received-response-back-to-the-waiting-consumer.md) -- DEFERRED
- [`INTER-3`: The consumer-facing CLI surface for asking and awaiting](./INTER-3-consumer-facing-cli-surface-for-asking-and-awaiting.md) -- DEFERRED
- [`INTER-4`: Default deep-link click behavior](./INTER-4-default-deep-link-click-behavior.md) -- DEFERRED
