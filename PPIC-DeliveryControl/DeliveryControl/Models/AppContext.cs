using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace DeliveryControl.Models;

public partial class AppContext : DbContext
{
    public AppContext()
    {
    }

    public AppContext(DbContextOptions<AppContext> options)
        : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Removed UseSqlite because the app exclusively uses SQL Server.
        // => optionsBuilder.UseSqlite("DataSource=app.db");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
