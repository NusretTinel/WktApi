namespace SimplePointApplication.Response.Result
{
    public class Response
    {
        public class Result
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public object Data { get; set; }
        }
    }
}
