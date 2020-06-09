﻿using Coldairarrow.Business.IT;
using Coldairarrow.Business.PB;
using Coldairarrow.Entity.IT;
using Coldairarrow.Entity.PB;
using Coldairarrow.Entity.TD;
using Coldairarrow.IBusiness.DTO;
using Coldairarrow.Util;
using EFCore.Sharding;
using LinqKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Threading.Tasks;

namespace Coldairarrow.Business.TD
{
    public partial class TD_OutStorageBusiness : BaseBusiness<TD_OutStorage>, ITD_OutStorageBusiness, ITransientDependency
    {
        public async Task<PageResult<TD_OutStorage>> GetDataListAsync(TD_OutStoragePageInput input)
        {
            var q = GetIQueryable()
                .Include(i => i.Customer)
                .Include(i => i.Address)
                .Include(i => i.CreateUser)
                .Include(i => i.AuditUser)
                .Where(w => w.StorageId == input.StorId);
            var where = LinqHelper.True<TD_OutStorage>();
            var search = input.Search;


            if (search.Status.HasValue)
                where = where.And(w => w.Status == search.Status.Value);
            if (!search.Code.IsNullOrEmpty())
                where = where.And(w => w.Code.Contains(search.Code));
            if (!search.OutType.IsNullOrEmpty())
                where = where.And(w => w.OutType == search.OutType);
            if (search.OutStorTimeStart.HasValue)
                where = where.And(w => w.OutTime >= search.OutStorTimeStart.Value);
            if (search.OutStorTimeEnd.HasValue)
                where = where.And(w => w.OutTime <= search.OutStorTimeEnd.Value);

            return await q.Where(where).GetPageResultAsync(input);
        }

        public async Task<TD_OutStorage> GetTheDataAsync(string id)
        {
            return await this.GetIQueryable()
                .Include(i => i.OutStorDetails)
                    .ThenInclude(t => t.Location)
                .Include(i => i.OutStorDetails)
                    .ThenInclude(t => t.Material)
                .Include(i => i.OutStorDetails)
                    .ThenInclude(t => t.Tray)
                .Include(i => i.OutStorDetails)
                    .ThenInclude(t => t.TrayZone)
                .SingleOrDefaultAsync(w => w.Id == id);
        }

        public async Task AddDataAsync(TD_OutStorage data)
        {
            if (data.Code.IsNullOrEmpty())
            {
                var codeSvc = _ServiceProvider.GetRequiredService<IPB_BarCodeTypeBusiness>();
                data.Code = await codeSvc.Generate("TD_OutStorage");
            }
            data.OutNum = data.OutStorDetails.Sum(s => s.OutNum);
            data.TotalAmt = data.OutStorDetails.Sum(s => s.TotalAmt);
            await InsertAsync(data);
        }

        public async Task UpdateDetailAsync(TD_OutStorage data)
        {
            //if (data.Code.IsNullOrEmpty())
            //{
            //    var codeSvc = _ServiceProvider.GetRequiredService<IPB_BarCodeTypeBusiness>();
            //    data.Code = await codeSvc.Generate("TD_OutStorage");
            //}
            data.OutNum = data.OutStorDetails.Sum(s => s.OutNum);
            data.TotalAmt = data.OutStorDetails.Sum(s => s.TotalAmt);
            await UpdateAsync(data);
        }

        public async Task UpdateDataAsync(TD_OutStorage data)
        {
            var curDetail = data.OutStorDetails;
            var listDetail = await Service.GetIQueryable<TD_OutStorDetail>().Where(w => w.OutStorId == data.Id).ToListAsync();

            var curIds = curDetail.Select(s => s.Id).ToList();
            var dbIds = listDetail.Select(s => s.Id).ToList();
            var deleteIds = dbIds.Except(curIds).ToList();
            var detailSvc = _ServiceProvider.GetRequiredService<ITD_OutStorDetailBusiness>();
            if (deleteIds.Count > 0)
                await detailSvc.DeleteDataAsync(deleteIds);

            var addIds = curIds.Except(dbIds).ToList();
            if (addIds.Count > 0)
            {
                var listAdds = curDetail.Where(w => addIds.Contains(w.Id)).ToList();
                await detailSvc.AddDataAsync(listAdds);
            }

            var updateIds = curIds.Except(addIds).ToList();
            if (updateIds.Count > 0)
            {
                var listUpdates = curDetail.Where(w => updateIds.Contains(w.Id)).ToList();
                await detailSvc.UpdateDataAsync(listUpdates);
            }

            data.OutNum = data.OutStorDetails.Sum(s => s.OutNum);
            data.TotalAmt = data.OutStorDetails.Sum(s => s.TotalAmt);

            await UpdateAsync(data);
        }


        [Transactional]
        public async Task Approve(AuditDTO audit)
        {
            var now = DateTime.Now;
            var data = await this.GetTheDataAsync(audit.Id);
            var detail = data.OutStorDetails;
            var dicMUnit = detail.Select(s => s.Material).Distinct().ToDictionary(k => k.Id, v => v.MeasureId);

            var lmSvc = _ServiceProvider.GetRequiredService<IIT_LocalMaterialBusiness>();
            var ldSvc = _ServiceProvider.GetRequiredService<IIT_LocalDetailBusiness>();

            // 减库存明细
            {
                //await UpdateAsync(data);
                var ldGrout = detail
                    .GroupBy(g => new { g.StorId, g.LocalId, g.TrayId, g.ZoneId, g.MaterialId, g.BatchNo, g.BarCode, g.Price })
                    .Select(s => new { s.Key.StorId, s.Key.LocalId, s.Key.TrayId, s.Key.ZoneId, s.Key.MaterialId, s.Key.BatchNo, s.Key.BarCode, s.Key.Price, TotalAmt = s.Sum(o => o.TotalAmt), Num = s.Sum(o => o.OutNum) })
                    .ToList();
                //查询当前库存明细记录
                var localIds = ldGrout.Select(s => s.LocalId).ToList();
                var trayIds = ldGrout.Select(s => s.TrayId).ToList();
                var zoneIds = ldGrout.Select(s => s.ZoneId).ToList();
                var materIds = ldGrout.Select(s => s.MaterialId).ToList();
                var batchNos = ldGrout.Select(s => s.BatchNo).ToList();
                var barCodes = ldGrout.Select(s => s.BarCode).ToList();

                var listLd = Service.GetIQueryable<IT_LocalDetail>();
                listLd = listLd.Where(w => w.StorId == audit.StorId);
                if (localIds.Count > 0)
                    listLd = listLd.Where(w => localIds.Contains(w.LocalId));
                if (trayIds.Count > 0)
                    listLd = listLd.Where(w => trayIds.Contains(w.TrayId));
                if (zoneIds.Count > 0)
                    listLd = listLd.Where(w => zoneIds.Contains(w.ZoneId));
                if (materIds.Count > 0)
                    listLd = listLd.Where(w => materIds.Contains(w.MaterialId));
                if (batchNos.Count > 0)
                    listLd = listLd.Where(w => batchNos.Contains(w.BatchNo));
                if (barCodes.Count > 0)
                    listLd = listLd.Where(w => barCodes.Contains(w.BarCode));
                var lmDbData = await listLd.ToListAsync();

                //处理库存明细
                var localMaterialSvc = _ServiceProvider.GetRequiredService<IIT_LocalDetailBusiness>();
                var listLmUpdate = new List<IT_LocalDetail>();
                var listDel = new List<IT_LocalDetail>();
                foreach (var lm in ldGrout)
                {
                    var lmNum = lm.Num;
                    var lds = lmDbData.Where(w => w.StorId == audit.StorId && w.LocalId == lm.LocalId && w.TrayId == lm.TrayId && w.ZoneId == lm.ZoneId && w.MaterialId == lm.MaterialId && w.BatchNo == lm.BatchNo && w.BarCode == lm.BarCode).OrderBy(o => o.InTime).ToList();
                    if (lds.Sum(s => s.Num) < lmNum) throw new Exception($"库存(明细)数量不够({lm.Num})");
                    foreach (var item in lds)
                    {
                        if (item.Num <= lmNum)
                        {
                            listDel.Add(item);
                            lmNum -= item.Num.GetValueOrDefault(0);
                        }
                        else
                        {
                            item.Num -= lmNum;
                            listLmUpdate.Add(item);
                            lmNum = 0;
                        }
                        if (lmNum == 0) break;
                    }
                }
                if (listDel.Count > 0) await ldSvc.DeleteDataAsync(listDel);
                if (listLmUpdate.Count > 0) await ldSvc.UpdateDataAsync(listLmUpdate);

            }

            // 减除库存
            {
                var lmGrout = detail
                    .GroupBy(g => new { g.StorId, g.LocalId, g.TrayId, g.ZoneId, g.MaterialId, g.BatchNo, g.BarCode })
                    .Select(s => new { s.Key.StorId, s.Key.LocalId, s.Key.TrayId, s.Key.ZoneId, s.Key.MaterialId, s.Key.BatchNo, s.Key.BarCode, Num = s.Sum(o => o.OutNum) })
                    .ToList();
                //查询当前库存记录
                var localIds = lmGrout.Select(s => s.LocalId).ToList();
                var trayIds = lmGrout.Select(s => s.TrayId).ToList();
                var zoneIds = lmGrout.Select(s => s.ZoneId).ToList();
                var materIds = lmGrout.Select(s => s.MaterialId).ToList();
                var batchNos = lmGrout.Select(s => s.BatchNo).ToList();
                var barCodes = lmGrout.Select(s => s.BarCode).ToList();

                var lmQuery = Service.GetIQueryable<IT_LocalMaterial>();
                lmQuery = lmQuery.Where(w => w.StorId == audit.StorId);
                if (localIds.Count > 0)
                    lmQuery = lmQuery.Where(w => localIds.Contains(w.LocalId));
                if (trayIds.Count > 0)
                    lmQuery = lmQuery.Where(w => trayIds.Contains(w.TrayId));
                if (zoneIds.Count > 0)
                    lmQuery = lmQuery.Where(w => zoneIds.Contains(w.ZoneId));
                if (materIds.Count > 0)
                    lmQuery = lmQuery.Where(w => materIds.Contains(w.MaterialId));
                if (batchNos.Count > 0)
                    lmQuery = lmQuery.Where(w => batchNos.Contains(w.BatchNo));
                if (barCodes.Count > 0)
                    lmQuery = lmQuery.Where(w => barCodes.Contains(w.BarCode));
                var lmDbData = await lmQuery.ToListAsync();

                //处理库存
                var listLmUpdate = new List<IT_LocalMaterial>();
                var listDel = new List<IT_LocalMaterial>();
                foreach (var lm in lmGrout)
                {
                    var lds = lmDbData.Where(w => w.StorId == audit.StorId && w.LocalId == lm.LocalId && w.TrayId == lm.TrayId && w.ZoneId == lm.ZoneId && w.MaterialId == lm.MaterialId && w.BatchNo == lm.BatchNo && w.BarCode == lm.BarCode).SingleOrDefault();
                    if (lds == null || lds.Num < lm.Num) throw new Exception($"没有找到对应物料的库存数据/库存数量不够({lm.Num})");
                    if (lds.Num == lm.Num) listDel.Add(lds);
                    else if (lds.Num > lm.Num)
                    {
                        lds.Num -= lm.Num;
                        listLmUpdate.Add(lds);
                    }
                }
                if (listDel.Count > 0) await lmSvc.DeleteDataAsync(listDel);
                if (listLmUpdate.Count > 0) await lmSvc.UpdateDataAsync(listLmUpdate);
            }

            // 添加台帐
            {
                var lmGrout = detail
                    .GroupBy(g => new { g.StorId, g.LocalId, g.TrayId, g.ZoneId, g.MaterialId, g.BatchNo, g.BarCode })
                    .Select(s => new { s.Key.StorId, s.Key.LocalId, s.Key.TrayId, s.Key.ZoneId, s.Key.MaterialId, s.Key.BatchNo, s.Key.BarCode, Num = s.Sum(o => o.OutNum) })
                    .ToList();

                var listRB = new List<IT_RecordBook>();
                foreach (var item in lmGrout)
                {
                    var rb = new IT_RecordBook();
                    rb.Id = IdHelper.GetId();
                    rb.RefCode = data.Code;
                    rb.Type = "Out";
                    rb.ToStorId = item.StorId;
                    rb.ToLocalId = item.LocalId;
                    rb.MaterialId = item.MaterialId;
                    rb.MeasureId = dicMUnit[item.MaterialId];
                    rb.BatchNo = item.BatchNo;
                    rb.BarCode = item.BarCode;
                    rb.Num = item.Num;
                    rb.CreateTime = now;
                    rb.CreatorId = audit.AuditUserId;
                    rb.Deleted = false;
                    listRB.Add(rb);
                }
                if (listRB.Count > 0)
                {
                    var bookSvc = _ServiceProvider.GetRequiredService<IIT_RecordBookBusiness>();
                    await bookSvc.AddDataAsync(listRB);
                }
            }

            // 处理托盘位置数据
            {
                var dicTray = detail
                    .GroupBy(g => new { g.LocalId, g.TrayId })
                    .Select(s => new { s.Key.LocalId, s.Key.TrayId })
                    .Distinct()
                    .ToDictionary(k => k.TrayId, v => v.LocalId);
                var trayIds = dicTray.Keys.ToList();
                if (trayIds.Count > 0)
                {
                    var listTray = await Service.GetIQueryable<PB_Tray>().Where(w => trayIds.Contains(w.Id)).ToListAsync();
                    foreach (var tray in listTray)
                    {
                        tray.LocalId = dicTray[tray.Id];
                    }
                    var traySvc = _ServiceProvider.GetRequiredService<IPB_TrayBusiness>();
                    await traySvc.UpdateDataAsync(listTray);
                }
            }

            // 修改出库状态 
            {
                data.Status = 1;
                data.AuditeTime = audit.AuditTime;
                data.AuditUserId = audit.AuditUserId;
                await UpdateAsync(data);
            }
        }

        public async Task Reject(AuditDTO audit)
        {
            var data = await this.GetEntityAsync(audit.Id);
            // 修改出库状态
            {
                data.Status = 2;
                data.AuditeTime = audit.AuditTime;
                data.AuditUserId = audit.AuditUserId;
                await UpdateAsync(data);
            }
        }
    }
}