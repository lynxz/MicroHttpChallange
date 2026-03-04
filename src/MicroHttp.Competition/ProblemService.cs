namespace MicroHttp.Competition
{
    public class ProblemService : IProblemService
    {

        public byte[] GetProblem(string userId)
        {
            return [(byte)65, (byte)66, (byte)67, (byte)68]; // Example problem data
        }

    }
}
