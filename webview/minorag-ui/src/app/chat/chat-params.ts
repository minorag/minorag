export interface ChatParams {
  topK: number;
  clientId: number | null;
  projectId: number | null;
  explicitRepoIds: number[];
  noLlm: boolean;
  verbose: boolean;
  allRepos: boolean;
  useAdvancedModel: boolean;
}
