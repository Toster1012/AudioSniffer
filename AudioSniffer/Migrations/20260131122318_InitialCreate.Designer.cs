using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using AudioSniffer.Data;

namespace AudioSniffer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260131122318_InitialCreate")]
    partial class InitialCreate
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "9.0.0");

            modelBuilder.Entity("AudioSniffer.Models.RequestHistory", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("datetime2");

                    b.Property<string>("AudioFileId")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<float>("OverallConfidence")
                        .HasColumnType("real");

                    b.Property<bool>("IsNeuralNetwork")
                        .HasColumnType("bit");

                    b.Property<string>("DetectionsJson")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<float>("DurationSeconds")
                        .HasColumnType("real");

                    b.Property<int>("SampleRate")
                        .HasColumnType("int");

                    b.Property<string>("Format")
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("Id");

                    b.ToTable("RequestHistories");
                });
#pragma warning restore 612, 618
        }
    }
}