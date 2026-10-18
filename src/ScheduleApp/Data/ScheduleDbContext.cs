using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ScheduleApp.Domain;
using ScheduleApp.Domain.Entities;

namespace ScheduleApp.Data
{
    public class ScheduleDbContext : DbContext
    {
        public ScheduleDbContext(DbContextOptions<ScheduleDbContext> options) : base(options)
        {
        }

        public DbSet<Auditorium> Auditoriums { get; set; }
        public DbSet<Teacher> Teachers { get; set; }
        public DbSet<Discipline> Disciplines { get; set; }
        public DbSet<StudentGroup> Groups { get; set; }
        public DbSet<TimeSlot> TimeSlots { get; set; }
        public DbSet<SchedulePeriod> Periods { get; set; }
        public DbSet<StudyPlanItem> StudyPlanItems { get; set; }
        public DbSet<TeacherPreference> TeacherPreferences { get; set; }
        public DbSet<Lesson> Lessons { get; set; }
        public DbSet<LessonGroup> LessonGroups { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Auditorium>(e =>
            {
                e.Property(x => x.Number).IsRequired().HasMaxLength(20);
                e.Property(x => x.Building).HasMaxLength(60);
                e.HasIndex(x => x.Number).IsUnique();
            });

            modelBuilder.Entity<Teacher>(e =>
            {
                e.Property(x => x.FullName).IsRequired().HasMaxLength(120);
                e.Property(x => x.Department).HasMaxLength(120);
                e.HasIndex(x => x.FullName);
            });

            modelBuilder.Entity<Discipline>(e =>
            {
                e.Property(x => x.Name).IsRequired().HasMaxLength(120);
                e.Property(x => x.ColorHex).HasMaxLength(9).HasDefaultValue("#4F81BD");
            });

            modelBuilder.Entity<StudentGroup>(e =>
            {
                e.Property(x => x.Name).IsRequired().HasMaxLength(30);
                e.HasIndex(x => x.Name).IsUnique();
            });

            modelBuilder.Entity<TimeSlot>(e =>
            {
                e.HasIndex(x => x.Order).IsUnique();
            });

            modelBuilder.Entity<SchedulePeriod>(e =>
            {
                e.Property(x => x.Name).IsRequired().HasMaxLength(120);
            });

            modelBuilder.Entity<StudyPlanItem>(e =>
            {
                e.HasOne(x => x.Discipline).WithMany().HasForeignKey(x => x.DisciplineId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
                e.HasIndex(x => new { x.GroupId, x.DisciplineId }).IsUnique();
            });

            modelBuilder.Entity<TeacherPreference>(e =>
            {
                e.HasOne(x => x.Teacher).WithMany(x => x.Preferences).HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.TimeSlot).WithMany().HasForeignKey(x => x.TimeSlotId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Auditorium).WithMany().HasForeignKey(x => x.AuditoriumId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Lesson>(e =>
            {
                e.HasOne(x => x.Period).WithMany().HasForeignKey(x => x.PeriodId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.TimeSlot).WithMany().HasForeignKey(x => x.TimeSlotId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Auditorium).WithMany().HasForeignKey(x => x.AuditoriumId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Discipline).WithMany().HasForeignKey(x => x.DisciplineId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne<StudyPlanItem>().WithMany().HasForeignKey(x => x.StudyPlanItemId).OnDelete(DeleteBehavior.SetNull);
                e.HasIndex(x => new { x.PeriodId, x.Day, x.TimeSlotId });
            });

            modelBuilder.Entity<LessonGroup>(e =>
            {
                e.HasKey(x => new { x.LessonId, x.GroupId });
                e.HasOne(x => x.Lesson).WithMany(x => x.LessonGroups).HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            });
        }
    }

    public static class DbConnectionInfo
    {
        public static string DatabasePath =>
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "schedule.db");

        public static string ConnectionString => $"Data Source={DatabasePath}";
    }
}
