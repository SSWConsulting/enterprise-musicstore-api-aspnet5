using System;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Serialization;
using Serilog;
using System.Threading.Tasks;
using Mindscape.Raygun4Net;

using Autofac;

#if NET461
using SerilogWeb.Classic.Enrichers;
#endif

using SSW.MusicStore.API.Filters;
using SSW.MusicStore.API.Infrastructure.DI;
using SSW.MusicStore.API.Settings;
using SSW.MusicStore.API.Infrastructure.Filters;
using Swashbuckle.Swagger.Model;

namespace SSW.MusicStore.API
{
    public class Startup
    {
        public Startup(IHostingEnvironment env)
        {

            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath);

            if (env.IsDevelopment())
            {
                builder.AddJsonFile("appsettings.json").AddJsonFile("privatesettings.json", optional: true);
                builder.AddUserSecrets();
            }
            else
            {
                builder.AddJsonFile("appsettings.json", optional: true);
            }

            builder.AddEnvironmentVariables();

            this.Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; set; }

        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            services.Configure<AppSettings>(options => Configuration.Bind(options));

            services.AddCors();

            services
                .AddMvc()
                .AddJsonOptions(
                    options =>
                        {
                            options.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
                        })
                .AddMvcOptions(
                    options =>
                    {
                        options.Filters.Add(new MvcLogActionFilter());
                        options.Filters.Add(new MvcExceptionActionFilter(this.Configuration));
                        options.Filters.Add(new MvcValidateModelActionFilter());
                    });

            services.AddRaygun(this.Configuration);
#if NET461
            services.AddApplicationInsightsTelemetry(this.Configuration);
#endif
            RegisterSwagger(services);

            var container = services.CreateAutofacContainer(this.Configuration);

            return container.Resolve<IServiceProvider>();
        }

        private static void RegisterSwagger(IServiceCollection services)
        {
            services.ConfigureSwaggerGen(opt =>
            {
                opt.SingleApiVersion(new Info
                {
                    Version = "v1",
                    Title = "Music Store API",
                    Description = "API for SSW Music Store"
                });

                opt.DescribeAllEnumsAsStrings();
            });

            services.AddSwaggerGen();
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            var config =
                new LoggerConfiguration()
                    .WriteTo.Seq(serverUrl: Configuration["Seq:Url"], apiKey: Configuration["Seq:Key"])
#if NET461
                    .Enrich.With(new HttpRequestIdEnricher())
#endif
                    .Enrich.WithProperty("ApplicationName", "Music Store");

            Log.Logger = config.CreateLogger();
            
            loggerFactory.AddSerilog();
            loggerFactory.AddDebug();

            if (env.IsDevelopment())
            {
                app.UseCors(policy => policy
                    .AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials());
            }
            else
            {
                app.UseCors(policy => policy
                            .WithOrigins(Configuration["Cors:Url"])
                            .AllowAnyMethod()
                            .AllowAnyHeader()
                            .AllowCredentials());
            }

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }

            app.UseStaticFiles();

            var jwtOptions = new JwtBearerOptions
            {
                Audience = Configuration["Auth0:ClientId"],
                Authority = $"https://{Configuration["Auth0:Domain"]}",
                Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        Log.Logger.Error("Authentication failed.", context.Exception);
                        return Task.FromResult(0);
                    }
                }
            };
            app.UseJwtBearerAuthentication(jwtOptions);

            // Note: this line must be after the OAuth config above
            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "",
                    defaults: new { controller = "Home", action = "Index" }
                );
            });
            app.UseSwaggerUi();
            app.UseSwagger();

            app.UseRaygun();
#if NET461
            app.UseApplicationInsightsRequestTelemetry();
            app.UseApplicationInsightsExceptionTelemetry();
#endif
        }
    }
}
