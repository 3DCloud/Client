namespace Client
{
    internal struct PrinterState
    {
        public bool IsConnected { get; init; }
        public bool IsPrinting { get; init; }
        public double[] HotendTemperatures { get; init; }
        public double? BedTemperature { get; init; }
    }
}
