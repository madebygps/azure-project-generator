using azure_project_generator.models;
using azure_project_generator.services;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;

namespace azure_project_generator
{
    public class GenerateProject
    {
        private readonly ILogger<GenerateProject> _logger;
        private readonly ContentGenerationService _contentGenerationService;

        public GenerateProject(ILogger<GenerateProject> logger, ContentGenerationService contentGenerationService)
        {
            _logger = logger;
            _contentGenerationService = contentGenerationService;
        }

        [Function("GenerateProject")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequestData req, string certificationCode, string skillName,
             [CosmosDBInput(Connection = "CosmosDBConnection")] CosmosClient client)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            _logger.LogInformation("C# HTTP trigger function processed a request.");

     
            string projectPrompt = "I need a project idea for the certification exam " + certificationCode + " for the skill " + skillName;
            
            float[] projectPromptVector = _contentGenerationService.GenerateEmbeddingsAsync(projectPrompt).Result;

            var queryDef = new QueryDefinition
                (query: $"SELECT c.serviceName, c.skillName, c.topicName, VectorDistance(c.contextVector,@embedding) " +
                $"AS SimilarityScore FROM c ORDER BY VectorDistance(c.contextVector,@embedding)"
                ).WithParameter("@embedding", projectPromptVector);

            using FeedIterator<CertificationService> resultSetIterator =
                 client.GetContainer("AzureCertDB", "certvectors").GetItemQueryIterator<CertificationService>(queryDef);

            string projectServices = "";

            while (resultSetIterator.HasMoreResults)
            {
                FeedResponse<CertificationService> feedResponse = resultSetIterator.ReadNextAsync().Result;
                foreach (var item in feedResponse)
                {
                    projectServices += item.ServiceName + " ";
                }
            }

            string cloudProjectIdea = await _contentGenerationService.GenerateProjectIdeaAsync(projectServices, skillName);

            response.Headers.Add("Content-Type", "application/json");
            await response.WriteStringAsync(cloudProjectIdea);

            return response;
        }
    }
}
