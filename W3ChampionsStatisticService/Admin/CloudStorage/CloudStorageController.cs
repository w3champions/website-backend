using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using W3C.Contracts.Admin.Rewards;

namespace W3ChampionsStatisticService.Rewards.CloudStorage
{
    [ApiController]
    [Route("api/admin/storage")]
    public class CloudStorageController : ControllerBase
    {
        [HttpGet]
        [HasContentPermission]
        public IActionResult ListFiles()
        {
            AlibabaService alibabaService = new AlibabaService();
            try {
                var fileList = alibabaService.ListFiles();
                return Ok(fileList);
            } catch (Exception ex) {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost("upload")]
        [HasContentPermission]
        public IActionResult UploadFile([FromBody] UploadFileRequest req)
        {
            AlibabaService alibabaService = new AlibabaService();
            try {
                var fileList = alibabaService.ListFiles();
                if (fileList.Select(f => f.Name).Contains(req.Name)) {
                    throw new ValidationException($"Could not upload file. File {req.Name} already exists.");
                }
                alibabaService.UploadFile(req);
                return Ok($"File {req.Name} uploaded!");
            } catch (ValidationException ex) {
                return BadRequest(ex.Message);
            } catch (Exception ex) {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpGet("download/{fileName}")]
        [HasContentPermission]
        public async Task<IActionResult> DownloadFile([FromRoute] string fileName)
        {
            AlibabaService alibabaService = new AlibabaService();
            try {
                var fileList = alibabaService.ListFiles();
                if (!fileList.Select(f => f.Name).Contains(fileName)) {
                    throw new ValidationException($"Could not download file. File {fileName} does not exist.");
                }
                var file = await alibabaService.DownloadFile(fileName);
                return File(file, "application/octet-stream", fileName);
            } catch (ValidationException ex) {
                return BadRequest(ex.Message);
            } catch (Exception ex) {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpDelete("{fileName}")]
        [HasContentPermission]
        public IActionResult DeleteFile([FromRoute] string fileName)
        {
            AlibabaService alibabaService = new AlibabaService();
            try {
                var fileList = alibabaService.ListFiles();
                if (!fileList.Select(f => f.Name).Contains(fileName)) {
                    throw new ValidationException($"Could not delete file. File {fileName} does not exist.");
                }
                alibabaService.DeleteFile(fileName);
                return Ok($"File {fileName} was deleted.");
            } catch (ValidationException ex) {
                return BadRequest(ex.Message);
            } catch (Exception ex) {
                return StatusCode(500, ex.Message);
            }
        }
    }
}
