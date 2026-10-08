
    // Debug information - tampilkan di console secara clean
    console.group("DASHBOARD v2.0 - MONITORING STARTED");
    console.log("Total Schedules Today:", @(ViewBag.TodaySchedules ?? 0));
    console.log("Completed:", @(ViewBag.CompletedCount ?? 0));
    console.log("In Progress:", @(ViewBag.InProgressCount ?? 0));
    console.log("DELAY PICKUP:", @(ViewBag.DelayPickupCount ?? 0));
    console.log("DELAY DOCK IN:", @(ViewBag.NotArrivedCount ?? 0));
    console.groupEnd();
