using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using System.ComponentModel.DataAnnotations;
using W3C.Contracts.Admin.CloudStorage;
using W3ChampionsStatisticService.Admin.CloudStorage.S3;
using W3ChampionsStatisticService.Admin.CloudStorage.Alibaba;
using W3C.Contracts.Admin.Permission;
using W3C.Domain.Tracing;
namespace W3ChampionsStatisticService.Admin.CloudStorage;

[ApiController]
[Route("api/admin/storage")]
[Trace]
public class CloudStorageController : ControllerBase
{
    [HttpGet("alibaba")]
    [BearerHasPermissionFilter(Permission = EPermission.Content)]
    public IActionResult ListAlibabaFiles()
    {
        AlibabaService alibabaService = new();
        try
        {
            var fileList = alibabaService.ListFiles();
            return Ok(fileList);
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }

    [HttpGet("s3")]
    [BearerHasPermissionFilter(Permission = EPermission.Content)]
    public async Task<IActionResult> ListS3Files()
    {
        S3Service s3Service = new();
        try
        {
            var fileList = await s3Service.ListFiles();
            return Ok(fileList);
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }

    [HttpPost("alibaba/upload")]
    [BearerHasPermissionFilter(Permission = EPermission.Content)]
    public IActionResult UploadAlibabaFile([FromBody][NoTrace] UploadFileRequest req)
    {
        AlibabaService alibabaService = new();
        try
        {
            var fileList = alibabaService.ListFiles();
            if (fileList.Select(f => f.Name).Contains(req.Name))
            {
                throw new ValidationException($"Could not upload file. File {req.Name} already exists.");
            }
            alibabaService.UploadFile(req);
            return Ok($"File {req.Name} uploaded!");
        }
        catch (ValidationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }

    [HttpPost("s3/upload")]
    [BearerHasPermissionFilter(Permission = EPermission.Content)]
    public async Task<IActionResult> UploadS3File([FromBody][NoTrace] UploadFileRequest req)
    {
        S3Service s3Service = new();
        try
        {
            var fileList = await s3Service.ListFiles();
            if (fileList.Select(f => f.Name).Contains(req.Name))
            {
                throw new ValidationException($"Could not upload file. File {req.Name} already exists.");
            }
            await s3Service.UploadFile(req);
            return Ok($"File {req.Name} uploaded!");
        }
        catch (ValidationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }

    [HttpGet("alibaba/download/{fileName}")]
    [BearerHasPermissionFilter(Permission = EPermission.Content)]
    public async Task<IActionResult> DownloadAlibabaFile([FromRoute] string fileName)
    {
        AlibabaService alibabaService = new();
        try
        {
            var fileList = alibabaService.ListFiles();
            if (!fileList.Select(f => f.Name).Contains(fileName))
            {
                throw new ValidationException($"Could not download file. File {fileName} does not exist.");
            }
            var file = await alibabaService.DownloadFile(fileName);
            return File(file, "application/octet-stream", fileName);
        }
        catch (ValidationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }

    [HttpGet("s3/download/{fileName}")]
    [BearerHasPermissionFilter(Permission = EPermission.Content)]
    public async Task<IActionResult> DownloadS3File([FromRoute] string fileName)
    {
        S3Service s3Service = new();
        try
        {
            var fileList = await s3Service.ListFiles();
            if (!fileList.Select(f => f.Name).Contains(fileName))
            {
                throw new ValidationException($"Could not download file. File {fileName} does not exist.");
            }
            var file = await s3Service.DownloadFile(fileName);
            return File(file, "application/octet-stream", fileName);
        }
        catch (ValidationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }

    [HttpDelete("alibaba/{fileName}")]
    [BearerHasPermissionFilter(Permission = EPermission.Content)]
    public IActionResult DeleteAlibabaFile([FromRoute] string fileName)
    {
        AlibabaService alibabaService = new();
        try
        {
            var fileList = alibabaService.ListFiles();
            if (!fileList.Select(f => f.Name).Contains(fileName))
            {
                throw new ValidationException($"Could not delete file. File {fileName} does not exist.");
            }
            alibabaService.DeleteFile(fileName);
            return Ok($"File {fileName} was deleted.");
        }
        catch (ValidationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }

    [HttpDelete("s3/{fileName}")]
    [BearerHasPermissionFilter(Permission = EPermission.Content)]
    public async Task<IActionResult> DeleteS3File([FromRoute] string fileName)
    {
        S3Service s3Service = new();
        try
        {
            var fileList = await s3Service.ListFiles();
            if (!fileList.Select(f => f.Name).Contains(fileName))
            {
                throw new ValidationException($"Could not delete file. File {fileName} does not exist.");
            }
            await s3Service.DeleteFile(fileName);
            return Ok($"File {fileName} was deleted.");
        }
        catch (ValidationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }
}
