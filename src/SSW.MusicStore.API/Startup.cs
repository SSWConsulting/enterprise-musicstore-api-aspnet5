﻿using System;
using System.Collections.Generic;

using Microsoft.AspNet.Authentication.JwtBearer;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Diagnostics.Entity;
using Microsoft.AspNet.Hosting;
using Microsoft.Data.Entity;
using Microsoft.Dnx.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Serialization;
using Serilog;
using SSW.MusicStore.API.Models;
using SSW.MusicStore.API.Services;
using SSW.MusicStore.API.Services.Query;
using System.Threading.Tasks;
using System.Linq;
using System.Linq.Expressions;

using Autofac;
using Autofac.Extensions.DependencyInjection;

using SerilogWeb.Classic.Enrichers;

using SSW.MusicStore.API.Services.Command;
using SSW.MusicStore.API.Services.Command.Interfaces;
using SSW.MusicStore.API.Services.Query.Interfaces;
using Microsoft.Extensions.PlatformAbstractions;

using SSW.MusicStore.DependencyResolution;

using ServiceDescriptor = Microsoft.Extensions.DependencyInjection.ServiceDescriptor;

namespace SSW.MusicStore.API
{
    public class Startup
    {
        public Startup(IHostingEnvironment env, IApplicationEnvironment appEnv)
        {

            var builder = new ConfigurationBuilder()
                .SetBasePath(appEnv.ApplicationBasePath);

            if (env.IsDevelopment())
            {
                builder.AddJsonFile("appsettings.json").AddJsonFile("privatesettings.json");
            }
            else
            {
                builder.AddJsonFile("appsettings.json", optional: true);
            }

            builder.AddUserSecrets();
            builder.AddEnvironmentVariables();
            this.Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; set; }

        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            services.AddEntityFramework()
                .AddSqlServer()
                .AddDbContext<MusicStoreContext>(options =>
                options.UseSqlServer(Configuration["Data:DefaultConnection:ConnectionString"]));

            services.AddCors();

            services.AddMvc().AddJsonOptions(
                opt =>
                {
                    opt.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
                });

            var builder = new ContainerBuilder();

            builder.RegisterModule(new DataModule(Configuration["Data:DefaultConnection:ConnectionString"]));
            builder.RegisterType<AuthMessageSender>()
                .As<IEmailSender>().InstancePerLifetimeScope();
            builder.RegisterType<DbContextFactory>()
                .As<IDbContextFactory<MusicStoreContext>>().InstancePerLifetimeScope();
            builder.RegisterType<GenreQueryService>()
                .As<IGenreQueryService>().InstancePerLifetimeScope();
            builder.RegisterType<AlbumQueryService>()
                .As<IAlbumQueryService>().InstancePerLifetimeScope();
            builder.RegisterType<CartQueryService>()
                .As<ICartQueryService>().InstancePerLifetimeScope();
            builder.RegisterType<CartCommandService>()
                .As<ICartCommandService>().InstancePerLifetimeScope();

            //Populate the container with services that were previously registered
            builder.Populate(services);

            var container = builder.Build();

            return container.Resolve<IServiceProvider>();


            //services.AddTransient<IEmailSender, AuthMessageSender>();
            //services.AddTransient<IDbContextFactory<MusicStoreContext>, DbContextFactory>();
            //services.AddTransient<IGenreQueryService, GenreQueryService>();
            //services.AddTransient<IAlbumQueryService, AlbumQueryService>();
            //services.AddTransient<ICartQueryService, CartQueryService>();
            //services.AddTransient<ICartCommandService, CartCommandService>();
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            var config =
                new LoggerConfiguration()
                    .WriteTo.ColoredConsole()
                    .WriteTo.Seq(serverUrl: Configuration["Seq:Url"], apiKey: Configuration["Seq:Key"])
                    .Enrich.WithProperty("ApplicationName", "Music Store")
                    .Enrich.With(new HttpRequestIdEnricher());
            Log.Logger = config.CreateLogger();

            loggerFactory.MinimumLevel = LogLevel.Information;
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
                app.UseDatabaseErrorPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }

            app.UseIISPlatformHandler();

            app.UseStaticFiles();

            app.UseJwtBearerAuthentication(options =>
            {
                options.Audience = Configuration["Auth0:ClientId"];
                options.Authority = $"https://{Configuration["Auth0:Domain"]}";
                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        Log.Logger.Error("Authentication failed.", context.Exception);
                        return Task.FromResult(0);
                    }
                };
            });

            // Note: this line must be after the OAuth config above
            app.UseMvc();

            //Slows web api - only do this on first run to popular db
            SampleData.InitializeMusicStoreDatabaseAsync(app.ApplicationServices).Wait();
        }
    }
}
