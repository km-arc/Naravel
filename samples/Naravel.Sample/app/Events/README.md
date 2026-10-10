# Application Events

Application event types live here. `OrderPlaced` is the sample event dispatched by `EventsDemoController` through
`Naravel.Events`; it is a small serializable record because the queued listener receives a JSON copy in the worker.

Listeners are in `app/Listeners`, and they are registered in `config/EventsSampleConfig.cs`. This is separate from
the queue's own lifecycle events (`IQueueEventListener`), which describe job processing, not application activity.
See `docs/en/events.md` for the full behavior and `samples/Naravel.Sample/README.md` for how to run the demo.
