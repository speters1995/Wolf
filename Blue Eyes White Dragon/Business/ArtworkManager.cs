﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Blue_Eyes_White_Dragon.Business.Factory.Interface;
using Blue_Eyes_White_Dragon.Business.Interface;
using Blue_Eyes_White_Dragon.Business.Models;
using Blue_Eyes_White_Dragon.DataAccess;
using Blue_Eyes_White_Dragon.DataAccess.Interface;
using Blue_Eyes_White_Dragon.UI.Models;
using Blue_Eyes_White_Dragon.Utility;
using Blue_Eyes_White_Dragon.Utility.Interface;

namespace Blue_Eyes_White_Dragon.Business
{
    public class ArtworkManager : IArtworkManager
    {
        private readonly IFileRepository _fileRepo;
        private readonly ICardRepository _cardRepo;
        /// <summary>
        /// Points to an error image located in the users temp directory.
        /// A rather hacky way to supply a string path to the artwork model
        /// </summary>
        private readonly ILogger _logger;
        private readonly ICardDbContextFactory _cardDbFactory;

        public ArtworkManager(IFileRepository fileRepo, ICardRepository cardRepo,
            ILogger logger, ICardDbContextFactory cardDbFactory)
        {
            _fileRepo = fileRepo;
            _cardRepo = cardRepo;
            _logger = logger;
            _cardDbFactory = cardDbFactory;
        }

        public List<Artwork> CreateArtworkModels(List<Card> gameCards, DirectoryInfo gameImagesLocation, DirectoryInfo replacementImagesLocation)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            var artworkList = new ConcurrentBag<Artwork>();

            Parallel.For(0, gameCards.Count, i =>
            {
                var gameCard = gameCards[i];
                var gameImageFile = _fileRepo.FindImageFile(gameCard, gameImagesLocation);

                artworkList.Add(new Artwork()
                {
                    GameImageFile = gameImageFile,
                    GameImageMonsterName = gameCard.Name,
                    GameImagesDir = gameImagesLocation,
                    ReplacementImagesDir = replacementImagesLocation,
                    IsMatched = false
                });
            });
            stopwatch.Stop();
            _logger.LogInformation($"Created {gameCards.Count} ArtworkModels in {MiliToSec(stopwatch.ElapsedMilliseconds)}s");

            return artworkList.ToList();
        }

        private long MiliToSec(long stopwatchElapsedMilliseconds)
        {
            return stopwatchElapsedMilliseconds / 1000;
        }

        public List<Artwork> UpdateArtworkModelsWithReplacement(List<Artwork> artworkList)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            var numberOfArtwork = artworkList.Count;
            long numberProcessed = 0;

            foreach (var artwork in artworkList)
            {
                ProcessArtwork(artwork);
                numberProcessed++;
                _logger.LogInformation($"{numberProcessed} of {numberOfArtwork} processed - {artwork.GameImageMonsterName}");
            }
            stopwatch.Stop();
            _logger.LogInformation($"Processed {artworkList.Count} in {MiliToSec(stopwatch.ElapsedMilliseconds)}s");

            return artworkList;
        }

        private void ProcessArtwork(Artwork artwork)
        {
            var replacementCard = FindSuitableReplacementCard(artwork);
            artwork.ReplacementImageMonsterName = replacementCard.GameImageMonsterName;
            artwork.ReplacementImageFile = replacementCard.ReplacementImageFile;
            artwork.IsMatched = true;
        }

        private Artwork FindSuitableReplacementCard(Artwork artwork)
        {
            var matchingCards = SearchCards(artwork);
            var replacementCard = matchingCards.FirstOrDefault();

            if (replacementCard == null)
            {
                return HandleNoMatchFound(artwork);
            }

            if (matchingCards.Count == 1)
            {
                HandleSingleMatchFound(replacementCard, artwork);
            }

            if (matchingCards.Count > 1)
            {
                HandleMultipleMatchesFound(matchingCards, artwork, replacementCard);
            }

            return artwork;
        }

        private List<Card> SearchCards(Artwork artwork)
        {
            try
            {
                return _cardRepo.SearchCards(artwork.GameImageMonsterName);
            }
            catch (Exception e)
            {
                _logger.LogInformation($"Databas error: {e}, inner: {e.InnerException} for {artwork.GameImageMonsterName}");
                return new List<Card>();
            }
        }

        private void HandleSingleMatchFound(Card replacementCard, Artwork artwork)
        {
            var imageFile = _fileRepo.FindImageFile(replacementCard, artwork.ReplacementImagesDir);
            artwork.ReplacementImageFile = imageFile;
            artwork.IsMatched = true;
        }

        private void HandleMultipleMatchesFound(List<Card> matchingCards, Artwork artwork, Card replacementCard)
        {
            //TODO Gotta implement a way to show more than one card if multiple are found
            HandleSingleMatchFound(replacementCard, artwork);
            _logger.LogInformation($"{matchingCards.Count} matching cards found for {artwork.GameImageMonsterName} picked: {artwork.ReplacementImageFileName}");
        }

        private Artwork HandleNoMatchFound(Artwork artwork)
        {
            artwork.ReplacementImageMonsterName = artwork.GameImageMonsterName;
            artwork.ReplacementImageFile = _fileRepo.ErrorImage;
            artwork.IsMatched = false;
            _logger.LogInformation($"No match was found for {artwork.GameImageMonsterName} - picking the error image");
            return artwork;
        }
    }
}
