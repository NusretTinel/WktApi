using APP;
using APP.Interface;
using APP.Repository;
using APP.Service;
using APP.UnýtOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OSGeo.GDAL;
using OSGeo.OGR;
using SimplePointApplication.Controllers;



var builder = WebApplication.CreateBuilder(args);
GdalConfiguration.ConfigureGdal();
Gdal.AllRegister();
Ogr.RegisterAll();
// Add services to the container.
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp",
        builder => builder.WithOrigins("http://localhost:3000")
                         .AllowAnyMethod()
                         .AllowAnyHeader());
});



builder.Services.AddControllers();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<CwtEfService>();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("PostgreSQL"),
        x => x.UseNetTopologySuite()));

builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();
app.UseCors("AllowReactApp");
app.Run();
