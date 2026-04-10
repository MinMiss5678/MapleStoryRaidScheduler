using Newtonsoft.Json;
using Utils.JsonConverters;
using Xunit;

namespace Test;

public class BigIntStringConverterTests
{
    private readonly BigIntStringConverter _converter = new();

    // CanConvert
    [Theory]
    [InlineData(typeof(long), true)]
    [InlineData(typeof(ulong), true)]
    [InlineData(typeof(long?), true)]
    [InlineData(typeof(ulong?), true)]
    [InlineData(typeof(int), false)]
    [InlineData(typeof(string), false)]
    public void CanConvert_ReturnsCorrectResult(Type type, bool expected)
    {
        Assert.Equal(expected, _converter.CanConvert(type));
    }

    // WriteJson - null
    [Fact]
    public void WriteJson_NullValue_WritesNull()
    {
        var serialized = JsonConvert.SerializeObject(new { Value = (long?)null }, new JsonSerializerSettings
        {
            Converters = { _converter }
        });
        Assert.Contains("null", serialized);
    }

    // WriteJson - long value
    [Fact]
    public void WriteJson_LongValue_WritesStringRepresentation()
    {
        var obj = new WrapperLong { Value = 9007199254740993L };
        var serialized = JsonConvert.SerializeObject(obj, new JsonSerializerSettings
        {
            Converters = { _converter }
        });
        Assert.Contains("9007199254740993", serialized);
    }

    // WriteJson - ulong value
    [Fact]
    public void WriteJson_UlongValue_WritesStringRepresentation()
    {
        var obj = new WrapperUlong { Value = 18446744073709551615UL };
        var serialized = JsonConvert.SerializeObject(obj, new JsonSerializerSettings
        {
            Converters = { _converter }
        });
        Assert.Contains("18446744073709551615", serialized);
    }

    // ReadJson - null token
    [Fact]
    public void ReadJson_NullToken_ReturnsNull()
    {
        var result = JsonConvert.DeserializeObject<long?>("null", new JsonSerializerSettings
        {
            Converters = { _converter }
        });
        Assert.Null(result);
    }

    // ReadJson - long
    [Fact]
    public void ReadJson_LongString_ReturnsLong()
    {
        var json = "\"9007199254740993\"";
        var result = JsonConvert.DeserializeObject<long>(json, new JsonSerializerSettings
        {
            Converters = { _converter }
        });
        Assert.Equal(9007199254740993L, result);
    }

    // ReadJson - ulong
    [Fact]
    public void ReadJson_UlongString_ReturnsUlong()
    {
        var json = "\"123456789012345678\"";
        var result = JsonConvert.DeserializeObject<ulong>(json, new JsonSerializerSettings
        {
            Converters = { _converter }
        });
        Assert.Equal(123456789012345678UL, result);
    }

    // ReadJson - nullable long
    [Fact]
    public void ReadJson_NullableLongString_ReturnsNullableLong()
    {
        var json = "\"42\"";
        var result = JsonConvert.DeserializeObject<long?>(json, new JsonSerializerSettings
        {
            Converters = { _converter }
        });
        Assert.Equal(42L, result);
    }

    // ReadJson - nullable ulong
    [Fact]
    public void ReadJson_NullableUlongString_ReturnsNullableUlong()
    {
        var json = "\"99\"";
        var result = JsonConvert.DeserializeObject<ulong?>(json, new JsonSerializerSettings
        {
            Converters = { _converter }
        });
        Assert.Equal(99UL, result);
    }

    // ReadJson - empty string
    [Fact]
    public void ReadJson_EmptyString_ReturnsNull()
    {
        var result = JsonConvert.DeserializeObject<long?>($"\"\"", new JsonSerializerSettings
        {
            Converters = { _converter }
        });
        Assert.Null(result);
    }

    private class WrapperLong
    {
        [JsonConverter(typeof(BigIntStringConverter))]
        public long Value { get; set; }
    }

    private class WrapperUlong
    {
        [JsonConverter(typeof(BigIntStringConverter))]
        public ulong Value { get; set; }
    }
}
