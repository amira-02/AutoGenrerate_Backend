markdown# 🤖 AutoGenerate — AI-Powered Social Media Post Generator

AutoGenerate is a full-stack web application that allows users to generate AI-powered social media posts using a prompt or a JSON file, and publish them across multiple platforms.

---

## 🚀 Tech Stack

### Frontend
- **React** + **TypeScript** (TSX)
- **Vite** — build tool
- **Tailwind CSS** — styling
- **React Router DOM** — navigation
- **Framer Motion** — animations
- **Axios** — HTTP client
- **JWT Decode** — token decoding
- **React Icons** + **Lucide React** — icons

### Backend
- **.NET 8** — ASP.NET Core Web API
- **Entity Framework Core** — ORM
- **SQL Server** — database
- **JWT Authentication** — secure auth
- **Groq AI** — text generation
- **HuggingFace API** — image generation
- **SMTP** — email OTP service

---

## 📁 Project Structure
```
AutoGenerate/
    Backend/
    ├── appsettings.json
    ├── appsettings.Development.json
    ├── Program.cs
    ├── AutoGenerate.csproj
    ├── Dockerfile
    │
    ├── AiService/
    │   ├── AiController.cs
    │   ├── AiRecommendation.cs
    │   └── RecommendationsRequestDto.cs
    │
    ├── AuthService/
    │   ├── AuthController.cs
    │   ├── EmailService.cs
    │   ├── JwtService.cs
    │   ├── LoginRequest.cs
    │   ├── OtpCode.cs
    │   ├── OtpService.cs
    │   ├── RegisterRequest.cs
    │   └── VerifyOtpRequest.cs
    │
    ├── CaptionService/
    │   ├── Caption.cs
    │   ├── CaptionController.cs
    │   ├── ChatDto.cs
    │   ├── ChatHistoryItem.cs
    │   ├── ConfirmCaptionDto.cs
    │   ├── GenerateCaptionDto.cs
    │   ├── SaveCaptionDto.cs
    │   ├── UpdateCaptionDto.cs
    │   ├── UpdateMediaDto.cs
    │   └── UpdatePostParamsDto.cs
    │
    ├── Enum/
    │   └── PostStatus.cs
    │
    ├── ExternalTasks/
    │   ├── AssignTaskDto.cs
    │   ├── ExternalTaskController.cs
    │   └── ExternalTaskDto.cs
    │
    ├── ImageService/
    │   ├── GenerateImageDto.cs
    │   ├── ImageController.cs
    │   ├── SaveImageUrlDto.cs
    │   └── SetImageUrlDto.cs
    │
    ├── Notifications/
    │   └── Notifications.cs
    │
    ├── PostService/
    │   ├── CreatePostDto.cs
    │   ├── PostDto.cs
    │   ├── PostResponseDto.cs
    │   ├── PostResultDto.cs
    │   ├── PostsController.cs
    │   ├── RescheduleRequest.cs
    │   ├── ScheduleDto.cs
    │   └── UpdateStatusDto.cs
    │
    ├── Shared/
    │   ├── Clients/
    │   │   └── ClientsController.cs
    │   ├── Converters/
    │   │   └── DateTimeUtcConverter.cs
    │   ├── Data/
    │   │   ├── AppDbContext.cs
    │   │   └── Migrations/
    │   │       ├── 20260225161029_Initial.cs
    │   │       ├── 20260326131556_AddPostsAndSocialAccounts.cs
    │   │       ├── ...  (14 migrations)
    │   │       ├── 20260513090000_AddTrelloBoardId.cs   ← nouveau
    │   │       └── AppDbContextModelSnapshot.cs
    │   └── Models/
    │       ├── Client.cs
    │       ├── ExternalTask.cs
    │       ├── Image.cs
    │       ├── Notification.cs
    │       ├── Platform.cs
    │       ├── Post.cs
    │       ├── Topic.cs
    │       └── User.cs
    │
    ├── SocialAccounts/
    │   ├── AccountsController.cs
    │   ├── SaveAccountDto.cs
    │   ├── SocialAccount.cs
    │   ├── Socialcontroller.cs
    │   └── UpdateTokenDto.cs
    │
    ├── Swagger/
    │   └── SwaggerFileOperationFilter.cs
    │
    ├── TopicService/
    │   ├── CreateTopicDto.cs
    │   └── TopicController.cs
    │
    └── Trello/                          ← nouveau
        └── TrelloWebhookController.cs



Frontend/
├── index.html
├── package.json
├── vite.config.ts
├── tsconfig.json
├── tsconfig.app.json
├── tsconfig.node.json
├── tailwind.config.js
├── postcss.config.js
├── vercel.json
├── .env.example
│
├── src/
│   ├── main.tsx
│   ├── App.tsx
│   ├── App.css
│   ├── index.css
│   ├── global.var.ts
│   ├── i18n.tsx
│   ├── vite-env.d.ts
│   │
│   ├── pages/
│   │   ├── Home.tsx
│   │   ├── Auth.tsx
│   │   ├── Dashboard.tsx
│   │   ├── Editor.tsx
│   │   └── VerifyOtp.tsx
│   │
│   ├── router/
│   │   ├── AppRouter.tsx
│   │   └── ProtectedRoute.tsx
│   │
│   ├── hooks/
│   │   ├── AuthContext.tsx
│   │   ├── ClientContext.tsx
│   │   ├── ThemeContext.tsx
│   │   ├── useAuth.ts
│   │   └── useScroll.ts
│   │
│   ├── config/
│   │   ├── api.config.ts
│   │   └── theme.config.tsx
│   │
│   ├── services/
│   │   └── api.ts
│   │
│   ├── helpers/
│   │   └── api.ts
│   │
│   ├── store/
│   │   └── store.ts
│   │
│   ├── Redux/
│   │   └── slices/
│   │       └── authSlice.ts
│   │
│   ├── lib/
│   │   └── utils.ts
│   │
│   ├── components/
│   │   ├── DashboardSection/
│   │   │   ├── Analytics/
│   │   │   │   ├── index.tsx
│   │   │   │   ├── OverviewTab.tsx
│   │   │   │   ├── ContentTab.tsx
│   │   │   │   ├── Analyse.tsx
│   │   │   │   ├── Airecommendations.tsx
│   │   │   │   ├── AnalyticsComponents.tsx
│   │   │   │   ├── analyticsHelpers.ts
│   │   │   │   ├── analyticsTypes.ts
│   │   │   │   ├── SocialIcons.tsx
│   │   │   │   ├── FacebookTab.tsx
│   │   │   │   ├── InstagramTab.tsx
│   │   │   │   ├── LinkedinTab.tsx
│   │   │   │   └── TiktokTab.tsx
│   │   │   │
│   │   │   ├── Calendar/
│   │   │   │   └── CalendarView.tsx
│   │   │   │
│   │   │   ├── Notifications/
│   │   │   │   ├── NotificationBell.tsx
│   │   │   │   └── ExternalTaskModal.tsx
│   │   │   │
│   │   │   ├── Posts/
│   │   │   │   ├── PostsView.tsx
│   │   │   │   ├── CreatePostModal.tsx
│   │   │   │   └── DetailPanel.tsx
│   │   │   │
│   │   │   ├── Topics/
│   │   │   │   ├── TopicsView.tsx
│   │   │   │   └── TopicDetailView.tsx
│   │   │   │
│   │   │   ├── SocialAccounts/
│   │   │   │   └── ConnectedAccountsView.tsx
│   │   │   │
│   │   │   └── Settings/
│   │   │       └── Settings.tsx
│   │   │
│   │   ├── HomeSection/
│   │   │   ├── AboutUs.tsx
│   │   │   ├── Contact.tsx
│   │   │   ├── DashboardPreview.tsx
│   │   │   ├── GeneratePost.tsx
│   │   │   ├── Phone.tsx
│   │   │   ├── PricingSection.tsx
│   │   │   └── ProductPreview.tsx
│   │   │
│   │   ├── NavigationBar/
│   │   │   └── NavBar.tsx
│   │   │
│   │   ├── Footer/
│   │   │   └── Footer.tsx
│   │   │
│   │   └── UI/
│   │       ├── AppInterface.tsx
│   │       ├── ContainerScroll.tsx
│   │       ├── FloatingIcon.tsx
│   │       ├── IgMock.tsx
│   │       ├── Smartphone.tsx
│   │       ├── cinematic-landing-hero.tsx
│   │       ├── map.tsx
│   │       ├── social-hub-circuit.tsx
│   │       ├── typewriter-effect.tsx
│   │       └── typewriter-effect-demo.tsx
│   │
│   └── assets/
│       ├── AboutUs.css
│       ├── Auth.css
│       ├── Editor.css
│       ├── GeneratePost.css
│       ├── Home.css
│       ├── NavBar.css
│       └── VerifyOtp.css


## ⚙️ Getting Started

### Prerequisites
- Node.js >= 18
- .NET 8 SDK
- SQL Server
- HuggingFace API Key
- Groq API Key

---

### 🖥️ Backend Setup

**1. Clone the repo**
```bash
git clone https://github.com/amira-02/Auto_Generate.git
cd Auto_Generate/Backend
```

**2. Configure `appsettings.json`**
```json
{
  "Jwt": {
    "Key": "YOUR_SECRET_KEY",
    "Issuer": "AutoGenerateApi",
    "Audience": "AutoGenerateClient"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Your SQL Server connection string"
  },
  "Groq": {
    "ApiKey": "your_groq_api_key"
  },
  "HuggingFace": {
    "ApiKey": "your_huggingface_api_key"
  }
}
```

**3. Apply migrations and run**
```bash
dotnet ef database update
dotnet run
```

API runs on: `https://localhost:7079`

---

### 🌐 Frontend Setup

**1. Navigate to Frontend**
```bash
cd Auto_Generate/Frontend
```

**2. Install dependencies**
```bash
npm install
```

**3. Start the dev server**
```bash
npm run dev
```

App runs on: `http://localhost:5173`

---

## 🔐 Authentication Flow
```
Register → OTP sent to email → Verify OTP → JWT token → Access Dashboard
Login    → JWT token         → Access Dashboard
```

- JWT stored in `localStorage`
- Role-based access (`Editor`, `Admin`)
- Protected routes redirect to `/auth` if not authenticated

---

## ✨ Features

- 🔐 **Auth** — Register, Login, OTP Email Verification
- 🤖 **AI Post Generation** — Generate posts from a prompt or JSON file
- 🖼️ **AI Image Generation** — Generate images via HuggingFace Stable Diffusion
- 📊 **Dashboard** — View stats, recent posts, platform breakdown, activity chart
- 🌙 **Dark / Light Mode** — Global theme toggle persisted across all pages
- 📱 **Responsive** — Works on desktop and mobile
- 🚀 **Protected Routes** — Dashboard only accessible when authenticated

---

## 🌍 API Endpoints

| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| POST | `/api/auth/register` | Register new user | ❌ |
| POST | `/api/auth/login` | Login user | ❌ |
| POST | `/api/auth/verify-otp` | Verify OTP code | ❌ |
| POST | `/api/auth/resend-otp` | Resend OTP | ❌ |
| POST | `/api/ai/generate` | Generate post text | ✅ |
| POST | `/api/ai/generate-image` | Generate image | ✅ |

---

## 🎨 Theme System

The app uses a global `ThemeContext` for dark/light mode:
```tsx
import { useTheme } from "../hooks/ThemeContext";

const { t, isDark, toggleTheme } = useTheme();

// Use t.bg, t.text, t.card, t.border etc. for consistent theming
```

---

## 📦 Environment Variables

Create a `.env` file in `Frontend/` if needed:
```env
VITE_API_URL=https://localhost:7079/api
```

And update `services/api.ts`:
```ts
const API = axios.create({
  baseURL: import.meta.env.VITE_API_URL,
});
```

---

## 🤝 Contributing

1. Fork the project
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

---

## 👩‍💻 Author

**Amira** — [@amira-02](https://github.com/amira-02)

---

## 📄 License

This project is licensed under the MIT License.