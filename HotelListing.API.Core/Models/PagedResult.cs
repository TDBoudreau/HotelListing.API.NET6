namespace HotelListing.API.Core.Models
{
    public class PagedResult<T>
    {
        public int TotalCount { get; set; } //total number of records in that particular table
        public int PageNumber { get; set; } //what page the user is on
        public int RecordNumber { get; set; } //how many records are coming back
        public List<T> Items { get; set; } //actual items 
    }
}
