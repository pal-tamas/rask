using System.Text.Json;
using Rask.Generators.Shared;

namespace Rask.Data.Tests.Naming;

/// <summary>Rask's names, word by word: the snake case, Rails' plural, the 63-byte cut.</summary>
public sealed class StoreNamesTests
{
    [Theory]
    [InlineData("Fueling", "fuelings")]
    [InlineData("DeliveryNote", "delivery_notes")]
    [InlineData("Company", "companies")]
    [InlineData("Day", "days")]
    [InlineData("Person", "people")]
    [InlineData("SalesPerson", "sales_people")]
    [InlineData("Child", "children")]
    [InlineData("Woman", "women")]
    [InlineData("Address", "addresses")]
    [InlineData("Box", "boxes")]
    [InlineData("Batch", "batches")]
    [InlineData("Wish", "wishes")]
    [InlineData("Status", "statuses")]
    [InlineData("Bus", "buses")]
    [InlineData("Alias", "aliases")]
    [InlineData("Analysis", "analyses")]
    [InlineData("Knife", "knives")]
    [InlineData("Wolf", "wolves")]
    [InlineData("Index", "indices")]
    [InlineData("Matrix", "matrices")]
    [InlineData("Mouse", "mice")]
    [InlineData("Ox", "oxen")]
    [InlineData("Quiz", "quizzes")]
    [InlineData("Tomato", "tomatoes")]
    [InlineData("Datum", "data")]
    [InlineData("Equipment", "equipment")]
    [InlineData("Information", "information")]
    [InlineData("Series", "series")]
    [InlineData("StoredFileBytes", "stored_file_bytes")]
    [InlineData("News", "news")]
    [InlineData("VehicleCategoryVehicle", "vehicle_category_vehicles")]
    [InlineData("OutboxMessage", "outbox_messages")]
    [InlineData("CacheEntry", "cache_entries")]
    public void A_table_is_the_class_in_snake_case_with_its_last_word_plural(string className, string table)
    {
        var named = StoreNames.Table(className);

        var expected = table;

        Assert.Equal(expected, named);
    }

    [Theory]
    [InlineData("PistolId")]
    [InlineData("CreatedAt")]
    [InlineData("VATNumber")]
    [InlineData("Sha256")]
    [InlineData("Address2Line")]
    [InlineData("IOStream")]
    [InlineData("Km_Start")]
    [InlineData("id")]
    public void Snake_case_is_what_system_text_json_writes(string name)
    {
        var json = JsonNamingPolicy.SnakeCaseLower.ConvertName(name);

        var named = StoreNames.SnakeCase(name);

        Assert.Equal(json, named);
    }

    [Fact]
    public void A_column_joins_each_step_of_its_path_in_snake_case()
    {
        string[] path = ["DeliveredKm", "StartReading"];

        var column = StoreNames.Column(path);

        Assert.Equal("delivered_km_start_reading", column);
    }

    [Fact]
    public void A_constraint_is_its_prefix_its_table_and_its_columns()
    {
        string[] columns = ["tenant_id", "email"];

        var name = StoreNames.Constraint(StoreNames.Unique, "users", columns);

        Assert.Equal("uq_users_tenant_id_email", name);
    }

    [Fact]
    public void A_name_of_multi_byte_letters_is_cut_by_its_bytes_not_its_letters()
    {
        var name = new string('ő', 40);

        var cut = StoreNames.Fit(name);

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(cut) <= StoreNames.MaxBytes);
        Assert.Matches("^ő+_[0-9a-f]{8}$", cut);
    }

    [Fact]
    public void A_name_that_fits_is_left_as_it_is()
    {
        var name = new string('a', StoreNames.MaxBytes);

        var cut = StoreNames.Fit(name);

        Assert.Equal(name, cut);
    }
}
