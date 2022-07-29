namespace HotelListing.API.Data
{
    public class Country
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string ShortName { get; set; }
        
        //One-to-Many relation (Country -> Hotel1,Hotel2,etc)
        public virtual IList<Hotel> Hotels { get; set; }
    }
}