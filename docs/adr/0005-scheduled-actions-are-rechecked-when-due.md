# Scheduled actions are re-checked when due, against the subject loaded then

An action scheduled for later must pass its checks when it's due, not only when the event arrived. When a scheduled action is due, `ExecuteScheduledAsync` takes the stored event and the app's current rules, and re-checks the rule's condition and the action's condition. `$current` is the event's subject loaded fresh (by the loader its event schema declares, `Subject(o => o.Number).LoadedBy("order")`), `$previous` is the event's previous state, and contexts load fresh. So `Status eq AwaitingPayment` means "still unpaid", and `changed Status` means "still different from before the event": a change undone by then cancels the action. A rule that was deleted, disabled or edited so that the action is gone skips it. Without a subject loader, the checks run against the stored event.

Pending actions therefore stop being wanted through the rules that created them, not through cancellation code: the app's scheduler job stays one generic call, and the rule author's conditions decide.

## Considered Options

- **Withdrawn keys** (a later event of the same type reports keys no longer wanted). Rejected: the change that makes an action unwanted usually arrives as another event type (`OrderUpdated` pays an `OrderPlaced` reminder), whose rules never run the original rule.
- **Explicit cancellation by the app before each event.** Rejected as the main mechanism: cancelling everything for a subject loses actions still wanted (an address change cancels the payment reminder), and cancelling selectively mirrors the rules' conditions in code. It stays possible through `ScheduledAction.Key`.
- **The subject as a context (`@order.Status`).** Rejected: rule authors would see two `Status` fields (the event's and the context's) and couldn't tell what each means.
- **The app reloading state in its job.** Rejected: one generic job would need a loader per event type in app code; the event schema is where the vocabulary already says what an event is about.
- **`$previous` as the event's current state when due** ("changed since scheduled"). Rejected: an undone change would still count as a change, notifying people about something that's no longer true.
- **No re-check of the rule's condition.** Rejected: edits, disabling and conditions on current data (`Total gte 500`) wouldn't apply to pending actions.
