using Terrajobst.ApiCatalog;

var catalog = ApiCatalogModel.Load(@"C:\Users\immo\Downloads\Catalog\apicatalog.dat");

{
    var net70 = catalog.Frameworks.Single(fx => fx.Name == "net7.0");
    var regKey = catalog.GetAllApis().Single(a => a.GetFullName() == "Microsoft.Win32.RegistryKey");
    var a = PlatformAvailability.Create(regKey, net70);
    Console.WriteLine($"== {regKey.GetFullName()} in {net70} ===============================");
    Console.WriteLine();
    Console.WriteLine(a);
}

{
    var regKey = catalog.GetAllApis().Single(a => a.GetFullName() == "Microsoft.Win32.RegistryKey");
    var a = PlatformAvailability.Create(regKey);
    Console.WriteLine($"== {regKey.GetFullName()} ===============================");
    Console.WriteLine();
    Console.WriteLine(a);
}

{
    var str = catalog.GetAllApis().Single(a => a.GetFullName() == "System.String");
    Console.WriteLine();
    Console.WriteLine($"== {str.GetFullName()} ===============================");
    foreach (var fx in catalog.Frameworks.Where(f => f.Assemblies.Any()))
    {
        Console.WriteLine();
        Console.WriteLine(fx.Name);
        Console.WriteLine(PlatformAvailability.Create(str, fx));
    }
}

{
    var platforms = @"C:\Users\immo\Downloads\platforms.csv";

    using var writer = new StreamWriter(platforms);
    writer.WriteLine("API;Support");

    foreach (var api in catalog.GetAllApis())
    {
        var availability = PlatformAvailability.Create(api);
        if (availability is null || availability.Kind == PlatformAvailabilityKind.Unlimited)
            continue;

        writer.WriteLine($"{api.GetFullName()};{availability}");
    }
}