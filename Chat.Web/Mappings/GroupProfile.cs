using AutoMapper;
using Chat.Web.Models;
using Chat.Web.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Chat.Web.Mappings
{
    public class GroupProfile : Profile
    {
        public GroupProfile()
        {
            CreateMap<Group, GroupViewModel>();
            CreateMap<GroupViewModel, Group>();
        }
    }
}
