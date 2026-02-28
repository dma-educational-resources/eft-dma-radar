using System.Net.Http.Headers;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace eft_dma_radar.Common.Misc.Data.TarkovMarket
{
    internal static class TarkovDevCore
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static async Task<TarkovDevQuery> QueryTarkovDevAsync()
        {
            var query = new Dictionary<string, string>()
            {
                { "query",
                """
                {
                  maps {
                    name
                    nameId
                    extracts {
                      name
                      faction
                      position { x, y, z }
                    }
                    transits {
                      description
                      position { x, y, z }
                    }
                  }
                  items {
                    id
                    name
                    shortName
                    width
                    height
                    sellFor {
                      vendor {
                        name
                      }
                      priceRUB
                    }
                    basePrice
                    avg24hPrice
                    historicalPrices {
                      price
                    }
                    categories {
                      name
                    }
                    iconLink
                    iconLinkFallback
                    imageLink
                    properties {
                      ... on ItemPropertiesWeapon {
                        caliber
                      }
                    }
                  }
                  questItems {
                    id
                    shortName
                  }
                  lootContainers {
                    id
                    normalizedName
                    name
                  }
                  tasks {
                    id
                    name
                    kappaRequired
                    objectives {
                      id
                      type
                      optional
                      description
                      maps {
                        id
                        name
                        normalizedName
                      }
                      ... on TaskObjectiveItem {
                        item {
                          id
                          name
                          shortName
                        }
                        zones {
                          id
                          map {
                            id
                            normalizedName
                            name
                          }
                          position {
                            y
                            x
                            z
                          }
                        }
                        requiredKeys {
                          id
                          name
                          shortName
                        }
                        count
                        foundInRaid
                      }
                      ... on TaskObjectiveMark {
                        id
                        description
                        markerItem {
                          id
                          name
                          shortName
                        }
                        maps {
                          id
                          normalizedName
                          name
                        }
                        zones {
                          id
                          map {
                            id
                            normalizedName
                            name
                          }
                          position {
                            y
                            x
                            z
                          }
                        }
                        requiredKeys {
                          id
                          name
                          shortName
                        }
                      }
                      ... on TaskObjectiveQuestItem {
                        id
                        description
                        requiredKeys {
                          id
                          name
                          shortName
                        }
                        maps {
                          id
                          normalizedName
                          name
                        }
                        zones {
                          id
                          map {
                            id
                            normalizedName
                            name
                          }
                          position {
                            y
                            x
                            z
                          }
                        }
                        requiredKeys {
                          id
                          name
                          shortName
                        }
                        questItem {
                          id
                          name
                          shortName
                          normalizedName
                          description
                        }
                        count
                      }
                      ... on TaskObjectiveBasic {
                        id
                        description
                        requiredKeys {
                          id
                          name
                          shortName
                        }
                        maps {
                          id
                          normalizedName
                          name
                        }
                        zones {
                          id
                          map {
                            id
                            normalizedName
                            name
                          }
                          position {
                            y
                            x
                            z
                          }
                        }
                        requiredKeys {
                          id
                          name
                          shortName
                        }
                      }
                      ... on TaskObjectiveShoot {
                        maps {
                          id
                          normalizedName
                          name
                        }
                        zones {
                          id
                          map {
                            id
                            normalizedName
                            name
                          }
                          outline {
                            x
                            y
                            z
                          }
                          position {
                            y
                            x
                            z
                          }
                        }
                      }
                    }
                  }
                }
                """
                }
            };
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(100));
            using var response = await SharedProgram.HttpClient.PostAsJsonAsync(
                requestUri: "https://api.tarkov.dev/graphql", 
                value: query,
                cancellationToken: cts.Token);
            response.EnsureSuccessStatusCode();
            return await JsonSerializer.DeserializeAsync<TarkovDevQuery>(await response.Content.ReadAsStreamAsync(), _jsonOptions);
        }
    }
}
