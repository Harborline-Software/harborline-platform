using Harborline.Blocks.Scheduling.Durable;

if (args.Length != 2 || args[0] is not ("write-wait" or "write-partial"))
{
    Console.Error.WriteLine("usage: restart-probe (write-wait|write-partial) <absolute-journal-path>");
    return 2;
}

var store = new FileJournalSchedulingStore(new() { JournalPath = args[1] });
await store.SaveDraftAsync("tenant:probe", "definition-1", 0, "{\"name\":\"durable\"}", "actor:server");
await store.SaveCalendarEntityAsync(new("tenant:probe", SchedulingCalendarEntityKind.OwnedCalendar, "calendar-1", "{\"default\":true}"));
await store.SaveCalendarEntityAsync(new("tenant:probe", SchedulingCalendarEntityKind.CalendarEvent, "event-1", "{\"title\":\"restart\"}"));
await store.SaveCalendarEntityAsync(new("tenant:probe", SchedulingCalendarEntityKind.ResourceAvailability, "party:1", "{\"timezone\":\"America/New_York\"}"));

if (args[0] == "write-partial")
{
    store.Dispose();
    await using var tail = new FileStream(args[1], FileMode.Append, FileAccess.Write, FileShare.None);
    await tail.WriteAsync(new byte[] { 0x48, 0x4c, 0x53 });
    tail.Flush(flushToDisk: true);
    return 0;
}

Console.WriteLine("ready");
Console.Out.Flush();
await Task.Delay(Timeout.InfiniteTimeSpan); // parent must Process.Kill; normal shutdown is not the proof.
return 0;
