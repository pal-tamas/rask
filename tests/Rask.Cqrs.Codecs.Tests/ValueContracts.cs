namespace Rask.Cqrs.Codecs.Tests;

// What a domain holds, sent as it is: value objects declared from Rask.Data's bases, typed ids and a record struct.
public sealed partial record DriverName(string Value) : Text(Value, MaxLength: 10);

public sealed partial record Litres(decimal Value) : Number(Value, Min: 1, Scale: 2);

public sealed partial record KmRange(int Start, int End) : Range(Start, End, Strict: true);

public readonly partial record struct VehicleId(int Value);

public readonly record struct TripKey(Guid Value);

public readonly record struct Odometer(int Km, int Metres);

public sealed record AssignDriver(
    VehicleId Vehicle,
    VehicleId? Spare,
    TripKey Trip,
    DriverName Driver,
    DriverName? CoDriver,
    Litres Fuel,
    KmRange Km,
    Odometer Reading,
    List<VehicleId> Fleet) : ICommand<VehicleId>;
