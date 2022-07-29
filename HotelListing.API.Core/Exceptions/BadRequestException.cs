namespace HotelListing.API.Core.Exceptions
{
    public class BadRequestException : ApplicationException
    {
        public BadRequestException(string name, object key) : base($"{name} ({key}) issued a bad request")
        {

        }
    }
}
